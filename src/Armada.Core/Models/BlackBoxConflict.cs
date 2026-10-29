namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Conflict section: conflicted files and directories and the missions that collided.
    /// </summary>
    public class BlackBoxConflict
    {
        /// <summary>
        /// Whether the failure is a merge conflict.
        /// </summary>
        public bool IsConflict { get; set; } = false;

        /// <summary>
        /// Conflicted files (repository-relative, forward slashes).
        /// </summary>
        [CollectionBound(BlackBoxLimits.MaxConflictedFiles, Reason = "Capped by BlackBoxRecordBounder; ConflictedFileTotal holds the real count")]
        public List<string> ConflictedFiles
        {
            get => _ConflictedFiles;
            set => _ConflictedFiles = value ?? new List<string>();
        }

        /// <summary>
        /// Total conflicted files before capping.
        /// </summary>
        public int ConflictedFileTotal { get; set; } = 0;

        /// <summary>
        /// Whether ConflictedFiles was cut.
        /// </summary>
        public bool ConflictedFilesTruncated { get; set; } = false;

        /// <summary>
        /// Conflicted files rolled up to directories, with counts.
        /// </summary>
        [CollectionBound(BlackBoxLimits.MaxConflictedDirectories, Reason = "Capped by BlackBoxRecordBounder")]
        public List<BlackBoxDirectoryCount> ConflictedDirectories
        {
            get => _ConflictedDirectories;
            set => _ConflictedDirectories = value ?? new List<BlackBoxDirectoryCount>();
        }

        /// <summary>
        /// Missions whose landed commits touched the same files.
        /// </summary>
        [CollectionBound(BlackBoxLimits.MaxConflictingMissions, Reason = "Capped by BlackBoxRecordBounder")]
        public List<BlackBoxConflictingMission> ConflictingMissions
        {
            get => _ConflictingMissions;
            set => _ConflictingMissions = value ?? new List<BlackBoxConflictingMission>();
        }

        /// <summary>
        /// Commits on the target since the mission started.
        /// </summary>
        [CollectionBound(BlackBoxLimits.MaxTargetCommits, Reason = "Capped by BlackBoxRecordBounder; TargetCommitTotal holds the real count")]
        public List<BlackBoxCommitRef> TargetCommitsSinceStart
        {
            get => _TargetCommitsSinceStart;
            set => _TargetCommitsSinceStart = value ?? new List<BlackBoxCommitRef>();
        }

        /// <summary>
        /// Total target commits since start before capping.
        /// </summary>
        public int TargetCommitTotal { get; set; } = 0;

        private List<string> _ConflictedFiles = new List<string>();
        private List<BlackBoxDirectoryCount> _ConflictedDirectories = new List<BlackBoxDirectoryCount>();
        private List<BlackBoxConflictingMission> _ConflictingMissions = new List<BlackBoxConflictingMission>();
        private List<BlackBoxCommitRef> _TargetCommitsSinceStart = new List<BlackBoxCommitRef>();

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxConflict()
        {
        }
    }
}
