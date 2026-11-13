namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// What the dispatch advisor found for one mission before it was created: overlap warnings, brief lint findings, and
    /// the dependency it set when the vessel serializes overlapping missions.
    /// </summary>
    public class MissionDispatchAdvice
    {
        #region Public-Members

        /// <summary>
        /// Overlap warnings, at most 50.
        /// </summary>
        [CollectionBound(BlackBoxInsightLimits.MaxOverlapWarnings, Reason = "Strongest first")]
        public List<MissionOverlapWarning> OverlapWarnings
        {
            get => _OverlapWarnings;
            set => _OverlapWarnings = value ?? new List<MissionOverlapWarning>();
        }

        /// <summary>
        /// Brief lint findings, at most 50.
        /// </summary>
        [CollectionBound(BlackBoxInsightLimits.MaxLintFindings, Reason = "Lint stops at 50")]
        public List<BriefLintFinding> BriefLintWarnings
        {
            get => _BriefLintWarnings;
            set => _BriefLintWarnings = value ?? new List<BriefLintFinding>();
        }

        /// <summary>
        /// The mission the advisor chained this one after, or null.
        /// </summary>
        public string? SerializedAfterMissionId { get; set; } = null;

        #endregion

        #region Private-Members

        private List<MissionOverlapWarning> _OverlapWarnings = new List<MissionOverlapWarning>();
        private List<BriefLintFinding> _BriefLintWarnings = new List<BriefLintFinding>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public MissionDispatchAdvice()
        {
        }

        #endregion
    }
}
