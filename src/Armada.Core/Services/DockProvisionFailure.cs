namespace Armada.Core.Services
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// Why the last dock provisioning for a mission failed: the provisioning step that failed and the exception, kept by
    /// DockService (whose ProvisionAsync returns null on failure) so the dispatcher can record it in the black box.
    /// </summary>
    public class DockProvisionFailure
    {
        #region Public-Members

        /// <summary>
        /// Mission the dock was provisioned for.
        /// </summary>
        public string MissionId { get; set; } = "";

        /// <summary>
        /// Provisioning step that failed (DockProvision, WorktreeCreate, BranchCreate or StartCommitRecord).
        /// </summary>
        public BlackBoxStepEnum Step { get; set; } = BlackBoxStepEnum.DockProvision;

        /// <summary>
        /// Exception, or null.
        /// </summary>
        public Exception? Exception { get; set; } = null;

        /// <summary>
        /// Worktree path being provisioned, or null.
        /// </summary>
        public string? WorktreePath { get; set; } = null;

        /// <summary>
        /// Whether the worktree directory existed when the failure was recorded, or null when unknown.
        /// </summary>
        public bool? WorktreeExisted { get; set; } = null;

        /// <summary>
        /// When the failure was recorded (UTC).
        /// </summary>
        public DateTime RecordedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
