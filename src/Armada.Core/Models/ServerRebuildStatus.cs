namespace Armada.Core.Models
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// Snapshot of an Admiral self-rebuild: its identity, the slot being built, the resolved source commit,
    /// lifecycle state, and the accumulated build log. Returned by the rebuild REST endpoints and persisted to
    /// disk so the freshly cut-over server can report the outcome of the rebuild that launched it. See
    /// docs/SERVER_REBUILD.md.
    /// </summary>
    public class ServerRebuildStatus
    {
        #region Public-Members

        /// <summary>
        /// Unique rebuild identifier (rbd_ prefix).
        /// </summary>
        public string RebuildId { get; set; } = Constants.IdGenerator.GenerateKSortable(Constants.RebuildIdPrefix, 24);

        /// <summary>
        /// Target slot name for this rebuild (yyyy-MM-dd_HHmmss_&lt;shortSha&gt;). Null until computed.
        /// </summary>
        public string? Slot { get; set; } = null;

        /// <summary>
        /// Slot name that was active before this rebuild, retained for rollback. Null when there was none.
        /// </summary>
        public string? PreviousSlot { get; set; } = null;

        /// <summary>
        /// Resolved source commit sha that was built. Null until resolved.
        /// </summary>
        public string? Sha { get; set; } = null;

        /// <summary>
        /// The git ref requested for the build (branch, tag, or sha). Null means the default branch HEAD.
        /// </summary>
        public string? Ref { get; set; } = null;

        /// <summary>
        /// Absolute path to the database backup taken before cutover. Null until the backup runs.
        /// </summary>
        public string? BackupPath { get; set; } = null;

        /// <summary>
        /// Database schema version observed before the rebuild built. Compared against the live schema version
        /// during rollback to decide whether the pre-rebuild backup must be restored (a migration occurred) or a
        /// plain slot relaunch suffices. Null when it could not be read.
        /// </summary>
        public int? PreRebuildSchemaVersion { get; set; } = null;

        /// <summary>
        /// Current lifecycle state.
        /// </summary>
        public ServerRebuildStatusEnum Status { get; set; } = ServerRebuildStatusEnum.Building;

        /// <summary>
        /// When the rebuild started (UTC).
        /// </summary>
        public DateTime StartedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// When the rebuild reached a terminal state (UTC). Null while in progress.
        /// </summary>
        public DateTime? CompletedUtc { get; set; } = null;

        /// <summary>
        /// Error context when <see cref="Status"/> is <see cref="ServerRebuildStatusEnum.Failed"/>. Null
        /// otherwise.
        /// </summary>
        public string? Error { get; set; } = null;

        /// <summary>
        /// Accumulated build log (git, dotnet publish, dashboard build output).
        /// </summary>
        public string Log { get; set; } = String.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ServerRebuildStatus()
        {
        }

        #endregion
    }
}
