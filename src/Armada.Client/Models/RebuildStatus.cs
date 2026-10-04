namespace Armada.Client.Models
{
    using System;

    /// <summary>
    /// Status of a server rebuild and its accumulated log.
    /// </summary>
    public class RebuildStatus
    {
        #region Public-Members

        /// <summary>
        /// Rebuild id, or null.
        /// </summary>
        public string? RebuildId { get; set; } = null;

        /// <summary>
        /// Slot being built, or null.
        /// </summary>
        public string? Slot { get; set; } = null;

        /// <summary>
        /// Previous slot, or null.
        /// </summary>
        public string? PreviousSlot { get; set; } = null;

        /// <summary>
        /// Commit, or null.
        /// </summary>
        public string? Sha { get; set; } = null;

        /// <summary>
        /// Ref, or null.
        /// </summary>
        public string? Ref { get; set; } = null;

        /// <summary>
        /// Pre-rebuild backup path, or null.
        /// </summary>
        public string? BackupPath { get; set; } = null;

        /// <summary>
        /// Building, CuttingOver, Succeeded, Failed, RolledBack, or none.
        /// </summary>
        public string Status { get; set; } = "none";

        /// <summary>
        /// Start time, or null.
        /// </summary>
        public DateTime? StartedUtc { get; set; } = null;

        /// <summary>
        /// Completion time, or null.
        /// </summary>
        public DateTime? CompletedUtc { get; set; } = null;

        /// <summary>
        /// Error, or null.
        /// </summary>
        public string? Error { get; set; } = null;

        /// <summary>
        /// Accumulated build log, or null.
        /// </summary>
        public string? Log { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public RebuildStatus()
        {
        }

        #endregion
    }
}
