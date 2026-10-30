namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// A mission whose work collided with the failed mission.
    /// </summary>
    public class BlackBoxConflictingMission
    {
        /// <summary>
        /// Mission, or null for a commit made outside Armada.
        /// </summary>
        public string? MissionId { get; set; } = null;

        /// <summary>
        /// Mission title, or null.
        /// </summary>
        public string? Title { get; set; } = null;

        /// <summary>
        /// Landed commit, or null.
        /// </summary>
        public string? CommitHash { get; set; } = null;

        /// <summary>
        /// When the commit landed (UTC), or null.
        /// </summary>
        public DateTime? LandedUtc { get; set; } = null;

        /// <summary>
        /// Files both changed.
        /// </summary>
        [CollectionBound(BlackBoxLimits.MaxOverlappingFiles, Reason = "Capped by BlackBoxRecordBounder; OverlappingFileTotal holds the real count")]
        public List<string> OverlappingFiles
        {
            get => _OverlappingFiles;
            set => _OverlappingFiles = value ?? new List<string>();
        }

        /// <summary>
        /// Total overlapping files before capping.
        /// </summary>
        public int OverlappingFileTotal { get; set; } = 0;

        /// <summary>
        /// Whether the commit has no Armada-Mission-Id trailer.
        /// </summary>
        public bool IsOutsideArmada { get; set; } = false;

        /// <summary>
        /// Commit author (for commits outside Armada), or null.
        /// </summary>
        public string? Author { get; set; } = null;

        private List<string> _OverlappingFiles = new List<string>();

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxConflictingMission()
        {
        }
    }
}
