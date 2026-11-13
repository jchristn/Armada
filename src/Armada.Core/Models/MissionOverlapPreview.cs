namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Result of an overlap preview: the new mission's likely files and every overlap with active missions, black box
    /// hotspots and reserved directories on the vessel, plus the brief lint findings. Nothing is dispatched or changed.
    /// </summary>
    public class MissionOverlapPreview
    {
        #region Public-Members

        /// <summary>
        /// Vessel (vsl_ prefix).
        /// </summary>
        public string VesselId { get; set; } = "";

        /// <summary>
        /// The vessel's SerializeOverlappingMissions setting: when true, dispatch would chain the mission after SuggestedDependsOnMissionId.
        /// </summary>
        public bool SerializeOverlappingMissions { get; set; } = false;

        /// <summary>
        /// The new mission's likely files and directories (directories end with a slash), first 200.
        /// </summary>
        [CollectionBound(BlackBoxInsightLimits.MaxLikelyFiles, Reason = "First likely files; LikelyFileCount has the total")]
        public List<string> LikelyFiles
        {
            get => _LikelyFiles;
            set => _LikelyFiles = value ?? new List<string>();
        }

        /// <summary>
        /// How many likely files and directories were found.
        /// </summary>
        public int LikelyFileCount { get; set; } = 0;

        /// <summary>
        /// Files named by the brief's scoped-files directive.
        /// </summary>
        public int ScopedFileCount { get; set; } = 0;

        /// <summary>
        /// Paths named in the brief that exist in the vessel (checked against the tracked files).
        /// </summary>
        public int BriefPathCount { get; set; } = 0;

        /// <summary>
        /// False when the vessel's tracked files could not be read (no local repository, Harbor not connected); paths in the brief are then not used.
        /// </summary>
        public bool TrackedFilesKnown { get; set; } = false;

        /// <summary>
        /// Overlaps, strongest first, at most 50.
        /// </summary>
        [CollectionBound(BlackBoxInsightLimits.MaxOverlapWarnings, Reason = "Strongest overlaps first; WarningCount has the total")]
        public List<MissionOverlapWarning> Warnings
        {
            get => _Warnings;
            set => _Warnings = value ?? new List<MissionOverlapWarning>();
        }

        /// <summary>
        /// How many overlaps were found.
        /// </summary>
        public int WarningCount { get; set; } = 0;

        /// <summary>
        /// The active mission dispatch would chain the new mission after, or null when nothing overlaps or a dependency is already set.
        /// </summary>
        public string? SuggestedDependsOnMissionId { get; set; } = null;

        /// <summary>
        /// Active missions on the vessel compared.
        /// </summary>
        public int ActiveMissionsCompared { get; set; } = 0;

        /// <summary>
        /// True when the vessel has more than 200 active missions and only the newest 200 were compared.
        /// </summary>
        public bool ActiveMissionsTruncated { get; set; } = false;

        /// <summary>
        /// The brief lint findings.
        /// </summary>
        public BriefLintResult? BriefLint { get; set; } = null;

        #endregion

        #region Private-Members

        private List<string> _LikelyFiles = new List<string>();
        private List<MissionOverlapWarning> _Warnings = new List<MissionOverlapWarning>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public MissionOverlapPreview()
        {
        }

        #endregion
    }
}
