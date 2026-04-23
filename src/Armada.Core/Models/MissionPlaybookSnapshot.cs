namespace Armada.Core.Models
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// Immutable playbook content captured for a mission at dispatch time.
    /// </summary>
    public class MissionPlaybookSnapshot
    {
        /// <summary>
        /// Source playbook identifier.
        /// </summary>
        public string? PlaybookId { get; set; } = null;

        /// <summary>
        /// Playbook filename.
        /// </summary>
        public string FileName { get; set; } = "";

        /// <summary>
        /// Optional description.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Captured markdown content.
        /// </summary>
        public string Content { get; set; } = "";

        /// <summary>
        /// Delivery mode resolved for this mission.
        /// </summary>
        public PlaybookDeliveryModeEnum DeliveryMode { get; set; } = PlaybookDeliveryModeEnum.InlineFullContent;

        /// <summary>
        /// Fully resolved absolute path used by the mission, when applicable.
        /// </summary>
        public string? ResolvedPath { get; set; } = null;

        /// <summary>
        /// Worktree-relative path when the snapshot was attached into the mission worktree.
        /// </summary>
        public string? WorktreeRelativePath { get; set; } = null;

        /// <summary>
        /// Last update timestamp of the source playbook at snapshot time.
        /// </summary>
        public DateTime? SourceLastUpdateUtc { get; set; } = null;
    }
}
