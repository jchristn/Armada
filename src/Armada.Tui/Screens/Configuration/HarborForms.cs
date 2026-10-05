namespace Armada.Tui.Screens.Configuration
{
    using System;
    using System.Globalization;
    using System.Linq;
    using Armada.Core.Models;
    using Armada.Tui.Modals;
    using Armada.Tui.Screens.Entities;
    using Armada.Tui.Services;
    using Armada.Tui.Widgets;

    /// <summary>
    /// Harbor dialogs (dashboard <c>Harbors.tsx</c>): the register/edit form (name, max concurrent jobs, enabled for
    /// routing) and the details view (status, enabled, capacity, platform, protocol, last seen, capabilities). Use on
    /// the UI loop.
    /// </summary>
    public static class HarborForms
    {
        #region Public-Methods

        /// <summary>
        /// Open the register/edit form.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="existing">Harbor to edit, or null to register.</param>
        /// <param name="onSaved">Runs on the UI loop with the saved harbor.</param>
        /// <returns>The dialog.</returns>
        public static FormDialog Open(TuiContext context, Harbor? existing, Action<Harbor> onSaved)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            EntityForm form = new EntityForm(context);
            InputField name = form.Text("Name", existing != null ? existing.Name : "New Harbor", "", true);
            InputField max = form.Number("Max Concurrent Jobs", existing?.MaxConcurrentJobs ?? 4, 1, 1000);
            CheckField enabled = form.Check("Enabled for routing", existing?.Enabled ?? true);
            form.MarkClean();

            Harbor? saved = null;
            return EntityUi.ShowForm(context, existing != null ? "Edit Harbor" : "Register Harbor", form, existing != null ? "Save Changes" : "Register Harbor", async ct =>
            {
                Harbor payload = new Harbor();
                payload.Name = name.Value.Trim();
                payload.MaxConcurrentJobs = EntityForm.IntValue(max) ?? 4;
                payload.Enabled = enabled.Value;
                saved = existing != null
                    ? await context.Client.UpdateHarborAsync(existing.Id, payload, ct).ConfigureAwait(false)
                    : await context.Client.CreateHarborAsync(payload, ct).ConfigureAwait(false);
                return null;
            }, () =>
            {
                if (saved == null) return;
                string text = existing != null
                    ? EntityUi.T(context, "Harbor \"{{name}}\" saved.", "name", saved.Name)
                    : EntityUi.T(context, "Harbor \"{{name}}\" registered.", "name", saved.Name);
                EntityUi.Toast(context, NotificationSeverityEnum.Success, text);
                onSaved?.Invoke(saved);
            });
        }

        /// <summary>
        /// Show a harbor's details with its capabilities.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="harbor">Harbor.</param>
        /// <returns>The modal.</returns>
        public static ViewerModal ShowDetails(TuiContext context, Harbor harbor)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (harbor == null) throw new ArgumentNullException(nameof(harbor));
            LinkDetailView view = new LinkDetailView();
            view.Row("ID", harbor.Id, t => t.Code);
            view.Row("Status", EntityUi.Badge(context, harbor.ConnectionStatus.ToString()), t => StatusBadge.Style(harbor.ConnectionStatus, t));
            view.Row("Enabled", harbor.Enabled ? context.Loc.T("Yes") : context.Loc.T("No"));
            view.Row("Capacity", harbor.MaxConcurrentJobs.ToString(CultureInfo.InvariantCulture));
            view.Row("Platform", Platform(harbor));
            view.Row("Protocol", EntityUi.Dash(harbor.ProtocolVersion));
            view.Row("Last Seen", harbor.LastSeenUtc.HasValue ? EntityUi.Date(context, harbor.LastSeenUtc) : context.Loc.T("Never"));
            view.Section("Capabilities");
            if (harbor.Capabilities.Count == 0) view.Row("Capabilities", "-");
            foreach (HarborCapability c in harbor.Capabilities)
            {
                string value = (c.Available ? "+ " : "x ") + c.Name + (c.Available ? "" : context.Loc.T(" (unavailable)")) + (String.IsNullOrEmpty(c.Detail) ? "" : "  " + c.Detail);
                view.Row(c.Name, value, t => c.Available ? t.Text : t.Muted);
            }

            ViewerModal modal = new ViewerModal(harbor.Name, view, context.Loc, context.Theme.Current);
            modal.HeightRatio = 0.6;
            modal.CopyRequested += (s, e) => context.Clipboard.Copy(Armada.Client.ArmadaJson.Serialize(harbor), "JSON");
            context.Modals.Show(modal);
            return modal;
        }

        /// <summary>
        /// "platform / architecture", or "-".
        /// </summary>
        /// <param name="harbor">Harbor.</param>
        /// <returns>Text.</returns>
        public static string Platform(Harbor harbor)
        {
            string joined = String.Join(" / ", new[] { harbor.OsPlatform, harbor.Architecture }.Where(s => !String.IsNullOrEmpty(s)));
            return joined.Length == 0 ? "-" : joined;
        }

        #endregion
    }
}
