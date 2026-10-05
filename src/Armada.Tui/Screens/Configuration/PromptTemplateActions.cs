namespace Armada.Tui.Screens.Configuration
{
    using System;
    using Armada.Client.Models;
    using Armada.Core.Models;
    using Armada.Tui.Screens.Entities;
    using Armada.Tui.Services;

    /// <summary>
    /// Prompt template actions shared by the Prompts tab and the template detail screen (dashboard
    /// <c>PromptTemplates.tsx</c> and <c>PromptTemplateDetail.tsx</c>): Duplicate and Reset to Default with the
    /// dashboard's confirmations. Use on the UI loop thread.
    /// </summary>
    public static class PromptTemplateActions
    {
        #region Public-Methods

        /// <summary>
        /// Duplicate a template ("Name (Copy)") and open the copy.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="template">Template.</param>
        public static void Duplicate(TuiContext context, PromptTemplate template)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (template == null) throw new ArgumentNullException(nameof(template));
            PromptTemplateCreateRequest request = new PromptTemplateCreateRequest();
            request.Name = PersonaForms.DuplicateName(template.Name);
            request.Category = template.Category;
            request.Content = template.Content;
            request.Active = template.Active;
            request.Description = EntityUi.Blank(template.Description);
            EntityUi.Run<PromptTemplate?>(context, ct => context.Client.CreatePromptTemplateAsync(request, ct), created =>
            {
                if (created == null) return;
                EntityUi.Toast(context, NotificationSeverityEnum.Success, EntityUi.T(context, "Template \"{{name}}\" duplicated.", "name", created.Name));
                context.Navigate("/prompt-templates/" + Uri.EscapeDataString(created.Name));
            }, "Duplicate failed.");
        }

        /// <summary>
        /// Ask to reset a built-in template to its default content.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="name">Template name.</param>
        /// <param name="fromDetail">Use the detail page's wording.</param>
        /// <param name="onReset">Runs on the UI loop with the reset template.</param>
        public static void Reset(TuiContext context, string name, bool fromDetail, Action<PromptTemplate?> onReset)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            string message = fromDetail
                ? EntityUi.T(context, "Reset \"{{name}}\" to its built-in default content? Your customizations will be lost.", "name", name)
                : EntityUi.T(context, "Reset template \"{{name}}\" to its built-in default content? Any custom edits will be lost.", "name", name);
            context.Confirm("Reset to Default", message, () =>
            {
                EntityUi.Run<PromptTemplate?>(context, ct => context.Client.ResetPromptTemplateAsync(name, ct), result =>
                {
                    string text = fromDetail ? context.Loc.T("Template reset to default.") : EntityUi.T(context, "Template \"{{name}}\" reset to default.", "name", name);
                    EntityUi.Toast(context, NotificationSeverityEnum.Success, text);
                    onReset?.Invoke(result);
                }, "Reset failed.");
            }, "Reset to Default");
        }

        #endregion
    }
}
