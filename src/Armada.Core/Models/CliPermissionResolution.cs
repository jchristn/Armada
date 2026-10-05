namespace Armada.Core.Models
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// The CLI permission policy a captain launch runs with, where it came from, and (when ApproveInArmada could not be
    /// honored) why it runs as Refuse instead.
    /// </summary>
    public class CliPermissionResolution
    {
        #region Public-Members

        /// <summary>
        /// The policy the resolution order selected.
        /// </summary>
        public CliPermissionPolicyEnum Requested { get; set; } = CliPermissionPolicyEnum.Refuse;

        /// <summary>
        /// The policy the launch actually uses (Requested, or Refuse after a fallback).
        /// </summary>
        public CliPermissionPolicyEnum Effective { get; set; } = CliPermissionPolicyEnum.Refuse;

        /// <summary>
        /// Where Requested came from.
        /// </summary>
        public CliPermissionPolicySourceEnum Source { get; set; } = CliPermissionPolicySourceEnum.ServerDefault;

        /// <summary>
        /// Why ApproveInArmada falls back to Refuse, or null when Effective equals Requested.
        /// </summary>
        public CliPermissionFallbackReasonEnum? FallbackReason { get; set; } = null;

        /// <summary>
        /// One-line explanation of the policy and where to change it (for logs and the UI).
        /// </summary>
        public string Note
        {
            get => _Note;
            set => _Note = value ?? String.Empty;
        }

        #endregion

        #region Private-Members

        private string _Note = String.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public CliPermissionResolution()
        {
        }

        #endregion
    }
}
