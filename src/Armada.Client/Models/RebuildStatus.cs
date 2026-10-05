namespace Armada.Client.Models
{
    using System;
    using System.Text.Json.Serialization;
    using Armada.Core.Enums;

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
        /// Rebuild state, or null when no rebuild has run (older servers sent the string "none", read as null).
        /// </summary>
        [JsonConverter(typeof(RebuildStatusValueConverter))]
        public ServerRebuildStatusEnum? Status { get; set; } = null;

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
