namespace Armada.Tui.Screens.Configuration
{
    using System;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Tui.Modals;
    using Armada.Tui.Screens.Entities;
    using Armada.Tui.Services;
    using Armada.Tui.Widgets;

    /// <summary>
    /// The skill create and edit form shared by the Skills tab and the skill detail screen (dashboard
    /// <c>Skills.tsx</c> modal): name, category, description, content (inline or <c>Ctrl+E</c> for <c>$EDITOR</c>),
    /// visibility scope, and active. Use on the UI loop.
    /// </summary>
    public static class SkillForms
    {
        #region Public-Methods

        /// <summary>
        /// Open the form.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="existing">Skill to edit, or null to create.</param>
        /// <param name="onSaved">Runs on the UI loop with the saved skill.</param>
        /// <returns>The dialog.</returns>
        public static FormDialog Open(TuiContext context, Skill? existing, Action<Skill> onSaved)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            EntityForm form = new EntityForm(context);
            InputField name = form.Text("Name", existing != null ? existing.Name : "Untitled Skill", "", true);
            InputField category = form.Text("Category", existing?.Category, "engineering");
            InputField description = form.Text("Description", existing?.Description);
            TextAreaField content = form.Area("Content", existing?.Content, 10, false, "Markdown or plain text injected into mission prompts for projects that attach this skill.");
            SelectField<string> scope = form.Scope(existing != null ? existing.Scope : ScopeRules.ResolveCreateScope(context.Session, null));
            CheckField active = form.Check("Active", existing?.Active ?? true);
            form.MarkClean();

            Skill? saved = null;
            return EntityUi.ShowForm(context, existing != null ? "Edit Skill" : "Create Skill", form, existing != null ? "Save Changes" : "Create Skill", async ct =>
            {
                Skill payload = new Skill();
                payload.Name = name.Value.Trim();
                payload.Category = EntityUi.Blank(category.Value);
                payload.Description = EntityUi.Blank(description.Value);
                payload.Content = content.Value;
                payload.Active = active.Value;
                payload.Scope = ScopeRules.ResolveCreateScope(context.Session, EntityForm.EnumValue(scope, ScopeEnum.TenantWide));
                saved = existing != null
                    ? await context.Client.UpdateSkillAsync(existing.Id, payload, ct).ConfigureAwait(false)
                    : await context.Client.CreateSkillAsync(payload, ct).ConfigureAwait(false);
                return null;
            }, () =>
            {
                if (saved == null) return;
                string text = existing != null
                    ? EntityUi.T(context, "Skill \"{{name}}\" saved.", "name", saved.Name)
                    : EntityUi.T(context, "Skill \"{{name}}\" created.", "name", saved.Name);
                EntityUi.Toast(context, NotificationSeverityEnum.Success, text);
                onSaved?.Invoke(saved);
            });
        }

        #endregion
    }
}
