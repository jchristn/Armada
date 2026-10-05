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
    /// Playbook create, edit, and duplicate shared by the Playbooks tab and the playbook detail screen (dashboard
    /// <c>Playbooks.tsx</c>): file name, description, Markdown content (inline or <c>Ctrl+E</c> for <c>$EDITOR</c>),
    /// visibility scope, and active. Use on the UI loop.
    /// </summary>
    public static class PlaybookForms
    {
        #region Public-Members

        /// <summary>
        /// Starting content of a new playbook (the dashboard's default).
        /// </summary>
        public const string DefaultContent = "# Playbook\n\nDescribe the rules the model must follow.\n";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Open the form.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="existing">Playbook to edit, or null to create.</param>
        /// <param name="onSaved">Runs on the UI loop with the saved playbook.</param>
        /// <returns>The dialog.</returns>
        public static FormDialog Open(TuiContext context, Playbook? existing, Action<Playbook> onSaved)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            EntityForm form = new EntityForm(context);
            InputField fileName = form.Text("File Name", existing != null ? existing.FileName : "NEW_PLAYBOOK.md", context.Loc.T("CSHARP_BACKEND_ARCHITECTURE.md"), true);
            InputField description = form.Text("Description", existing?.Description, context.Loc.T("Optional summary shown during playbook selection"));
            TextAreaField content = form.Area("Markdown Content", existing != null ? existing.Content : DefaultContent, 12);
            SelectField<string> scope = form.Scope(existing != null ? existing.Scope : ScopeRules.ResolveCreateScope(context.Session, null));
            CheckField active = form.Check("Active and selectable during dispatch", existing?.Active ?? true);
            form.MarkClean();

            Playbook? saved = null;
            return EntityUi.ShowForm(context, existing != null ? "Edit Playbook" : "Create Playbook", form, existing != null ? "Save Changes" : "Create Playbook", async ct =>
            {
                Playbook payload = new Playbook();
                payload.FileName = fileName.Value.Trim();
                payload.Description = EntityUi.Blank(description.Value);
                payload.Content = content.Value;
                payload.Active = active.Value;
                payload.Scope = ScopeRules.ResolveCreateScope(context.Session, EntityForm.EnumValue(scope, ScopeEnum.TenantWide));
                saved = existing != null
                    ? await context.Client.UpdatePlaybookAsync(existing.Id, payload, ct).ConfigureAwait(false)
                    : await context.Client.CreatePlaybookAsync(payload, ct).ConfigureAwait(false);
                return null;
            }, () =>
            {
                if (saved == null) return;
                string text = existing != null
                    ? EntityUi.T(context, "Playbook \"{{name}}\" saved.", "name", saved.FileName)
                    : EntityUi.T(context, "Playbook \"{{name}}\" created.", "name", saved.FileName);
                EntityUi.Toast(context, NotificationSeverityEnum.Success, text);
                onSaved?.Invoke(saved);
            });
        }

        /// <summary>
        /// Duplicate a playbook ("NAME (Copy).md") and open the copy.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="source">Playbook to copy.</param>
        /// <param name="keepScope">Request the source's scope (resolved for the user), as the Playbooks tab does.</param>
        public static void Duplicate(TuiContext context, Playbook source, bool keepScope)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (source == null) throw new ArgumentNullException(nameof(source));
            Playbook payload = new Playbook();
            payload.FileName = DuplicateFileName(source.FileName);
            payload.Description = source.Description;
            payload.Content = source.Content;
            payload.Active = source.Active;
            payload.Scope = keepScope ? ScopeRules.ResolveCreateScope(context.Session, source.Scope) : ScopeRules.ResolveCreateScope(context.Session, null);
            EntityUi.Run<Playbook?>(context, ct => context.Client.CreatePlaybookAsync(payload, ct), created =>
            {
                if (created == null) return;
                EntityUi.Toast(context, NotificationSeverityEnum.Success, EntityUi.T(context, "Playbook \"{{name}}\" duplicated.", "name", created.FileName));
                context.Navigate("/playbooks/" + created.Id);
            }, "Duplicate failed.");
        }

        /// <summary>
        /// The dashboard's duplicate file name: "NAME (Copy).ext".
        /// </summary>
        /// <param name="fileName">File name.</param>
        /// <returns>Copy name.</returns>
        public static string DuplicateFileName(string fileName)
        {
            string trimmed = (fileName ?? "").Trim();
            if (trimmed.Length == 0) return "Copy";
            int dot = trimmed.LastIndexOf('.');
            if (dot > 0) return trimmed.Substring(0, dot) + " (Copy)" + trimmed.Substring(dot);
            return trimmed + " (Copy)";
        }

        #endregion
    }
}
