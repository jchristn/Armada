namespace Armada.Core.Models
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// Body of a CLI permission policy change on a captain or an Ask thread. A null policy clears the override, so the
    /// next level of the resolution order applies.
    /// </summary>
    public class CliPermissionPolicyUpdateRequest
    {
        #region Public-Members

        /// <summary>
        /// New policy, or null to clear the override.
        /// </summary>
        public CliPermissionPolicyEnum? Policy { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public CliPermissionPolicyUpdateRequest()
        {
        }

        #endregion
    }
}
