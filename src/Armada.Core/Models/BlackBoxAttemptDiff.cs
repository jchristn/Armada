namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// What changed between two black box entries (two attempts of one mission, or any two entries the caller can see):
    /// configuration, captain, runtime, model, base commit, changed files, failure kind and step.
    /// </summary>
    public class BlackBoxAttemptDiff
    {
        #region Public-Members

        /// <summary>
        /// First entry (bbx_ prefix).
        /// </summary>
        public string EntryId { get; set; } = "";

        /// <summary>
        /// Other entry (bbx_ prefix).
        /// </summary>
        public string OtherEntryId { get; set; } = "";

        /// <summary>
        /// True when both entries belong to the same mission.
        /// </summary>
        public bool SameMission { get; set; } = false;

        /// <summary>
        /// Attempt number of the first entry.
        /// </summary>
        public int Attempt { get; set; } = 0;

        /// <summary>
        /// Attempt number of the other entry.
        /// </summary>
        public int OtherAttempt { get; set; } = 0;

        /// <summary>
        /// True when both entries have the same failure-mode fingerprint.
        /// </summary>
        public bool SameFingerprint { get; set; } = false;

        /// <summary>
        /// Fields that differ, in section order, at most 100.
        /// </summary>
        [CollectionBound(BlackBoxInsightLimits.MaxDiffFields, Reason = "At most 100 differences; DifferenceCount has the total")]
        public List<BlackBoxDiffField> Differences
        {
            get => _Differences;
            set => _Differences = value ?? new List<BlackBoxDiffField>();
        }

        /// <summary>
        /// How many fields differ.
        /// </summary>
        public int DifferenceCount { get; set; } = 0;

        /// <summary>
        /// Files the first entry's mission changed and the other's did not, first 200.
        /// </summary>
        [CollectionBound(BlackBoxInsightLimits.MaxDiffFiles, Reason = "First 200; FilesOnlyInEntryCount has the total")]
        public List<string> FilesOnlyInEntry
        {
            get => _FilesOnlyInEntry;
            set => _FilesOnlyInEntry = value ?? new List<string>();
        }

        /// <summary>
        /// How many files only the first entry's mission changed.
        /// </summary>
        public int FilesOnlyInEntryCount { get; set; } = 0;

        /// <summary>
        /// Files the other entry's mission changed and the first's did not, first 200.
        /// </summary>
        [CollectionBound(BlackBoxInsightLimits.MaxDiffFiles, Reason = "First 200; FilesOnlyInOtherCount has the total")]
        public List<string> FilesOnlyInOther
        {
            get => _FilesOnlyInOther;
            set => _FilesOnlyInOther = value ?? new List<string>();
        }

        /// <summary>
        /// How many files only the other entry's mission changed.
        /// </summary>
        public int FilesOnlyInOtherCount { get; set; } = 0;

        /// <summary>
        /// Files both changed.
        /// </summary>
        public int FilesInBothCount { get; set; } = 0;

        /// <summary>
        /// False when either entry has no changed-file rows (no diff was available).
        /// </summary>
        public bool ChangedFilesKnown { get; set; } = false;

        #endregion

        #region Private-Members

        private List<BlackBoxDiffField> _Differences = new List<BlackBoxDiffField>();
        private List<string> _FilesOnlyInEntry = new List<string>();
        private List<string> _FilesOnlyInOther = new List<string>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxAttemptDiff()
        {
        }

        #endregion
    }
}
