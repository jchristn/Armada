namespace Armada.Core.Services
{
    using System;

    /// <summary>
    /// Raised when a dock (git worktree) could not be provisioned for a vessel. The underlying cause is logged by the
    /// dock service; this carries the vessel and branch so callers do not parse the message.
    /// </summary>
    public class DockProvisioningException : InvalidOperationException
    {
        #region Public-Members

        /// <summary>
        /// Vessel id.
        /// </summary>
        public string VesselId { get; }

        /// <summary>
        /// Branch the dock was to check out.
        /// </summary>
        public string BranchName { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="vesselId">Vessel id.</param>
        /// <param name="branchName">Branch name.</param>
        /// <param name="message">Human-readable message.</param>
        public DockProvisioningException(string vesselId, string branchName, string message)
            : base(message)
        {
            VesselId = vesselId ?? String.Empty;
            BranchName = branchName ?? String.Empty;
        }

        #endregion
    }
}
