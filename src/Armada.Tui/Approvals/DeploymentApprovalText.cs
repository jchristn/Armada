namespace Armada.Tui.Approvals
{
    using System;
    using Armada.Core.Models;
    using Armada.Tui.Services;

    /// <summary>
    /// The localized deployment approval label for the TUI: environment name first, deployment title second
    /// ("Deploy to production: Release 2.3 hotfix"), through <see cref="DeploymentApprovalLabel"/> and the shared
    /// catalog, so the Approvals queue, the Inbox, the confirmation dialogs, and notifications all read the same.
    /// </summary>
    public static class DeploymentApprovalText
    {
        #region Public-Methods

        /// <summary>
        /// Label from the deployment's fields.
        /// </summary>
        /// <param name="loc">Localizer, or null for English.</param>
        /// <param name="environmentName">Environment name, or null.</param>
        /// <param name="deploymentTitle">Deployment title, or null.</param>
        /// <param name="deploymentId">Deployment id (used when both the environment name and the title are missing).</param>
        /// <returns>Label.</returns>
        public static string Label(ITextLocalizer? loc, string? environmentName, string? deploymentTitle, string? deploymentId)
        {
            if (loc == null) return DeploymentApprovalLabel.Format(environmentName, deploymentTitle, deploymentId);
            return DeploymentApprovalLabel.Format(environmentName, deploymentTitle, deploymentId, (template, args) => loc.T(template, args));
        }

        /// <summary>
        /// Label for an inbox item of kind <see cref="InboxItemKinds.DeploymentApproval"/>. Uses the typed
        /// <see cref="InboxItem.EnvironmentName"/> and <see cref="InboxItem.DeploymentTitle"/>; an item from an older
        /// server without them falls back to <see cref="InboxItem.EntityName"/> as the environment (unless it is just
        /// the deployment id).
        /// </summary>
        /// <param name="loc">Localizer, or null for English.</param>
        /// <param name="inbox">Inbox item.</param>
        /// <returns>Label.</returns>
        public static string ForInbox(ITextLocalizer? loc, InboxItem inbox)
        {
            if (inbox == null) throw new ArgumentNullException(nameof(inbox));
            string? environment = inbox.EnvironmentName;
            if (String.IsNullOrWhiteSpace(environment) && String.IsNullOrWhiteSpace(inbox.DeploymentTitle)
                && !String.Equals(inbox.EntityName, inbox.EntityId, StringComparison.Ordinal))
                environment = inbox.EntityName;
            return Label(loc, environment, inbox.DeploymentTitle, inbox.EntityId);
        }

        #endregion
    }
}
