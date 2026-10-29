namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Evidence section: names, sizes and excerpts of the evidence objects, plus small structured evidence.
    /// </summary>
    public class BlackBoxEvidenceIndex
    {
        /// <summary>
        /// Evidence objects.
        /// </summary>
        [CollectionBound(BlackBoxLimits.MaxEvidenceItems, Reason = "Capped by BlackBoxRecordBounder")]
        public List<BlackBoxEvidenceItem> Objects
        {
            get => _Objects;
            set => _Objects = value ?? new List<BlackBoxEvidenceItem>();
        }

        /// <summary>
        /// Stage check results.
        /// </summary>
        [CollectionBound(BlackBoxLimits.MaxCheckResults, Reason = "Capped by BlackBoxRecordBounder")]
        public List<StageCheckResult> CheckResults
        {
            get => _CheckResults;
            set => _CheckResults = value ?? new List<StageCheckResult>();
        }

        /// <summary>
        /// Papercuts reported during the attempt.
        /// </summary>
        [CollectionBound(BlackBoxLimits.MaxPapercuts, Reason = "Capped by BlackBoxRecordBounder")]
        public List<string> Papercuts
        {
            get => _Papercuts;
            set => _Papercuts = value ?? new List<string>();
        }

        /// <summary>
        /// Diff stat, or null.
        /// </summary>
        public BlackBoxDiffStat? DiffStat { get; set; } = null;

        /// <summary>
        /// Relevant events, newest first.
        /// </summary>
        [CollectionBound(BlackBoxLimits.MaxRelevantEvents, Reason = "Capped by BlackBoxRecordBounder")]
        public List<BlackBoxEventRef> RelevantEvents
        {
            get => _RelevantEvents;
            set => _RelevantEvents = value ?? new List<BlackBoxEventRef>();
        }

        private List<BlackBoxEvidenceItem> _Objects = new List<BlackBoxEvidenceItem>();
        private List<StageCheckResult> _CheckResults = new List<StageCheckResult>();
        private List<string> _Papercuts = new List<string>();
        private List<BlackBoxEventRef> _RelevantEvents = new List<BlackBoxEventRef>();

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxEvidenceIndex()
        {
        }
    }
}
