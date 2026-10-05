namespace Armada.Tui.Screens.Configuration
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
    /// Persona forms shared by the Personas tab and the persona detail screen (dashboard <c>Personas.tsx</c> and
    /// <c>PersonaDetail.tsx</c>): the create/edit modal (name, description, prompt template, scope), the detail edit
    /// modal (description, prompt template, default captain), and Duplicate. Use on the UI loop thread.
    /// </summary>
    public static class PersonaForms
    {
        #region Public-Methods

        /// <summary>
        /// Open the create (null) or edit form from the Personas tab.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="existing">Persona to edit, or null to create.</param>
        /// <param name="onSaved">Runs on the UI loop after a save.</param>
        public static void OpenListForm(TuiContext context, Persona? existing, Action onSaved)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            EntityUi.Run(context, ct => EntityLookups.PromptTemplatesAsync(context.Client, ct), templates =>
            {
                EntityForm form = new EntityForm(context);
                InputField? name = null;
                if (existing == null) name = form.Text("Name", "", "", true);
                TextAreaField description = form.Area("Description", existing?.Description, 3, false, null);
                description.Placeholder = "Optional description of this persona...";
                SelectField<string> template = RequiredSelect(form, "Prompt Template Name", TemplateOptions(templates), existing?.PromptTemplateName, "Select a template...");
                SelectField<string> scope = form.Scope(existing?.Scope ?? ScopeRules.ResolveCreateScope(context.Session, null));
                form.MarkClean();
                string title = existing != null ? "Edit Persona" : "Create Persona";
                string savedName = existing?.Name ?? "";
                EntityUi.ShowForm(context, title, form, "Save", async ct =>
                {
                    ScopeEnum chosenScope = EntityForm.EnumValue(scope, ScopeEnum.TenantWide);
                    if (existing != null)
                    {
                        Persona body = Copy(existing);
                        body.Description = EntityUi.Blank(description.Value) ?? "";
                        body.PromptTemplateName = template.Value ?? existing.PromptTemplateName;
                        body.Scope = chosenScope;
                        await context.Client.UpdatePersonaAsync(existing.Name, body, ct).ConfigureAwait(false);
                    }
                    else
                    {
                        Persona body = new Persona();
                        body.Name = name!.Value.Trim();
                        body.Description = EntityUi.Blank(description.Value);
                        body.PromptTemplateName = template.Value ?? "";
                        body.Scope = ScopeRules.ResolveCreateScope(context.Session, chosenScope);
                        savedName = body.Name;
                        await context.Client.CreatePersonaAsync(body, ct).ConfigureAwait(false);
                    }

                    return null;
                }, () =>
                {
                    string text = existing != null
                        ? EntityUi.T(context, "Persona \"{{name}}\" saved.", "name", savedName)
                        : EntityUi.T(context, "Persona \"{{name}}\" created.", "name", savedName);
                    EntityUi.Toast(context, NotificationSeverityEnum.Success, text);
                    onSaved?.Invoke();
                });
            }, "Failed to load personas.");
        }

        /// <summary>
        /// Open the detail edit form (description, prompt template, default captain).
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="persona">Persona.</param>
        /// <param name="templates">Prompt templates.</param>
        /// <param name="captains">Captains.</param>
        /// <param name="onSaved">Runs on the UI loop after a save.</param>
        public static void OpenDetailForm(TuiContext context, Persona persona, List<PromptTemplate> templates, List<Captain> captains, Action onSaved)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (persona == null) throw new ArgumentNullException(nameof(persona));
            EntityForm form = new EntityForm(context);
            TextAreaField description = form.Area("Description", persona.Description, 4);
            SelectField<string> template = RequiredSelect(form, "Prompt Template Name", TemplateOptions(templates), persona.PromptTemplateName, "Select a template...");
            SelectField<string> captain = form.Select("Default Captain", EntityLookups.Options(captains, c => c.Id, c => c.Name, c => c.Id), persona.DefaultCaptainId, "None (default routing)", false,
                "Pre-fills the per-step captain at dispatch and becomes the preferred captain for missions of this persona.");
            form.MarkClean();
            EntityUi.ShowForm(context, "Edit Persona", form, "Save", async ct =>
            {
                Persona body = Copy(persona);
                body.Description = description.Value;
                body.PromptTemplateName = template.Value ?? persona.PromptTemplateName;
                body.DefaultCaptainId = EntityUi.Blank(captain.Value);
                await context.Client.UpdatePersonaAsync(persona.Name, body, ct).ConfigureAwait(false);
                return null;
            }, () =>
            {
                EntityUi.Toast(context, NotificationSeverityEnum.Success, EntityUi.T(context, "Persona \"{{name}}\" saved.", "name", persona.Name));
                onSaved?.Invoke();
            });
        }

        /// <summary>
        /// Duplicate a persona ("Name (Copy)") and open the copy.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="persona">Persona.</param>
        public static void Duplicate(TuiContext context, Persona persona)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (persona == null) throw new ArgumentNullException(nameof(persona));
            EntityUi.Run<Persona?>(context, ct =>
            {
                Persona body = new Persona();
                body.Name = DuplicateName(persona.Name);
                body.Description = persona.Description;
                body.PromptTemplateName = persona.PromptTemplateName;
                return context.Client.CreatePersonaAsync(body, ct);
            }, created =>
            {
                if (created == null) return;
                EntityUi.Toast(context, NotificationSeverityEnum.Success, EntityUi.T(context, "Persona \"{{name}}\" duplicated.", "name", created.Name));
                context.Navigate("/personas/" + Uri.EscapeDataString(created.Name));
            }, "Duplicate failed.");
        }

        /// <summary>
        /// The dashboard's duplicate name: "Name (Copy)".
        /// </summary>
        /// <param name="name">Name.</param>
        /// <returns>Copy name.</returns>
        public static string DuplicateName(string? name)
        {
            string trimmed = (name ?? "").Trim();
            return trimmed.Length > 0 ? trimmed + " (Copy)" : "Copy";
        }

        /// <summary>
        /// Add a required select with a placeholder and no empty option (the field is invalid until a value is chosen).
        /// </summary>
        /// <param name="form">Form.</param>
        /// <param name="label">English label.</param>
        /// <param name="options">Options.</param>
        /// <param name="value">Initial value, or null.</param>
        /// <param name="placeholder">English placeholder.</param>
        /// <returns>The field.</returns>
        public static SelectField<string> RequiredSelect(EntityForm form, string label, List<SelectOption<string>> options, string? value, string placeholder)
        {
            if (form == null) throw new ArgumentNullException(nameof(form));
            SelectField<string> field = form.Select(label, options, value, null, true);
            field.Placeholder = placeholder;
            return field;
        }

        /// <summary>
        /// Options for prompt template names.
        /// </summary>
        /// <param name="templates">Templates.</param>
        /// <returns>Options.</returns>
        public static List<SelectOption<string>> TemplateOptions(IEnumerable<PromptTemplate>? templates)
        {
            return EntityLookups.Options(templates, t => t.Name, t => t.Name, t => t.Category);
        }

        #endregion

        #region Private-Methods


        private static Persona Copy(Persona source)
        {
            Persona copy = new Persona();
            copy.Id = source.Id;
            copy.TenantId = source.TenantId;
            copy.UserId = source.UserId;
            copy.Scope = source.Scope;
            copy.Name = source.Name;
            copy.Description = source.Description;
            copy.PromptTemplateName = source.PromptTemplateName;
            copy.IsBuiltIn = source.IsBuiltIn;
            copy.Active = source.Active;
            copy.DefaultCaptainId = source.DefaultCaptainId;
            copy.CreatedUtc = source.CreatedUtc;
            copy.LastUpdateUtc = source.LastUpdateUtc;
            return copy;
        }

        #endregion
    }
}
