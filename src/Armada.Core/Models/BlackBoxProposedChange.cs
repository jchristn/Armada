namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// A proposed Armada change written as a mission-ready brief: title, one paragraph change and the acceptance test,
    /// ranked by impact, with a severity that escalates with recurrence.
    /// </summary>
    public class BlackBoxProposedChange
    {
        /// <summary>
        /// Rank by impact, 1 is the most impactful.
        /// </summary>
        public int Rank { get; set; } = 1;

        /// <summary>
        /// Mission title for the Armada change.
        /// </summary>
        public string Title { get; set; } = "";

        /// <summary>
        /// One paragraph describing the change, written for the captain that will make it.
        /// </summary>
        public string Change { get; set; } = "";

        /// <summary>
        /// The test that proves the change (suite and case), or null.
        /// </summary>
        public string? AcceptanceTest { get; set; } = null;

        /// <summary>
        /// Severity after escalation by recurrence (fingerprint occurrences and Armada versions).
        /// </summary>
        public BlackBoxSeverityEnum Severity { get; set; } = BlackBoxSeverityEnum.Medium;

        /// <summary>
        /// Rule that proposed it, or null for a scope default.
        /// </summary>
        public string? RuleId { get; set; } = null;

        /// <summary>
        /// Armada source locations the change touches (repository-relative file:method).
        /// </summary>
        [CollectionBound(BlackBoxLimits.MaxSourcePointers, Reason = "Capped by BlackBoxRecordBounder")]
        public List<string> CodePointers
        {
            get => _CodePointers;
            set => _CodePointers = value ?? new List<string>();
        }

        /// <summary>
        /// The change as a mission brief for the Armada vessel (title, change, acceptance test, code pointers and the
        /// Armada build commit), or null.
        /// </summary>
        public string? MissionBrief { get; set; } = null;

        private List<string> _CodePointers = new List<string>();

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxProposedChange()
        {
        }
    }
}
