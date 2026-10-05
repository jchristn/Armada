namespace Armada.Core.Settings
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// Settings for CLI tool permissions: how a CLI captain's own tools (shell, web fetch, file tools outside accepted
    /// edits) are permitted when no Ask thread, vessel, or captain override applies, who may approve a permission
    /// prompt, and how long a prompt waits. All values apply live (to the next launch; a pending prompt keeps its
    /// expiry).
    /// </summary>
    public class CliPermissionSettings
    {
        #region Public-Members

        /// <summary>
        /// Server default for Ask turns and milestone narrations. Default ApproveInArmada: the CLI's permission prompts
        /// become CLI permission requests that an approver decides in Armada (runtimes without a prompt hook run as
        /// Refuse).
        /// </summary>
        public CliPermissionPolicyEnum AskDefaultPolicy { get; set; } = CliPermissionPolicyEnum.ApproveInArmada;

        /// <summary>
        /// Server default for missions whose captain has neither a CliPermissionPolicy nor an explicit autoApprove
        /// option, and whose vessel has no AutoApprove override. Default Bypass, which is the behavior before CLI tool
        /// permissions existed (captains ran with their bypass flag unless autoApprove was false): missions run
        /// unattended and a prompting default would hold every mission on approvals.
        /// </summary>
        public CliPermissionPolicyEnum MissionDefaultPolicy { get; set; } = CliPermissionPolicyEnum.Bypass;

        /// <summary>
        /// When true, the owner of the Ask thread or mission may decide its permission prompts (allow once or deny).
        /// When false (the default), only global admins and the tenant's tenant admins may decide. Remembering a
        /// decision as a rule always requires an admin.
        /// </summary>
        public bool AllowOwnerApproval { get; set; } = false;

        /// <summary>
        /// Seconds a permission prompt waits for a decision before it is denied as expired. Default 600 (10 minutes),
        /// minimum 10, maximum 3600; out-of-range values are clamped.
        /// </summary>
        public int PromptTimeoutSeconds
        {
            get => _PromptTimeoutSeconds;
            set => _PromptTimeoutSeconds = value < 10 ? 10 : (value > 3600 ? 3600 : value);
        }

        #endregion

        #region Private-Members

        private int _PromptTimeoutSeconds = 600;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate with defaults.
        /// </summary>
        public CliPermissionSettings()
        {
        }

        #endregion
    }
}
