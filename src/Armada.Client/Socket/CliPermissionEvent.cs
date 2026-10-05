namespace Armada.Client.Socket
{
    using System;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Payload of cli_permission.requested and cli_permission.resolved: a captain's CLI permission prompt was created or
    /// decided. The request carries the recipient's own CanDecide and CanRemember.
    /// </summary>
    public class CliPermissionEvent
    {
        #region Public-Members

        /// <summary>
        /// Request id (cpr_ prefix).
        /// </summary>
        public string? RequestId { get; set; } = null;

        /// <summary>
        /// Request status, or null when absent.
        /// </summary>
        public CliPermissionRequestStatusEnum? Status { get; set; } = null;

        /// <summary>
        /// The request, or null when absent.
        /// </summary>
        public CliPermissionRequest? Request { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public CliPermissionEvent()
        {
        }

        #endregion
    }
}
