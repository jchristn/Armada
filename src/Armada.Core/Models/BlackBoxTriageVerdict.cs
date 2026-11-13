namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Triage verdict with the rule votes behind it.
    /// </summary>
    public class BlackBoxTriageVerdict
    {
        /// <summary>
        /// Verdict.
        /// </summary>
        public BlackBoxTriageEnum Verdict { get; set; } = BlackBoxTriageEnum.Unknown;

        /// <summary>
        /// Confidence.
        /// </summary>
        public BlackBoxConfidenceEnum Confidence { get; set; } = BlackBoxConfidenceEnum.Low;

        /// <summary>
        /// Votes.
        /// </summary>
        [CollectionBound(BlackBoxLimits.MaxTriageVotes, Reason = "Capped by BlackBoxRecordBounder")]
        public List<BlackBoxTriageVote> Votes
        {
            get => _Votes;
            set => _Votes = value ?? new List<BlackBoxTriageVote>();
        }

        /// <summary>
        /// Candidates on a tie.
        /// </summary>
        [CollectionBound(BlackBoxLimits.MaxTriageCandidates, Reason = "One per BlackBoxTriageEnum value")]
        public List<BlackBoxTriageEnum> TiedWith
        {
            get => _TiedWith;
            set => _TiedWith = value ?? new List<BlackBoxTriageEnum>();
        }

        /// <summary>
        /// Whether the handoff and landing prior (BBX-PRIOR-ARMADA) took part in the vote.
        /// </summary>
        public bool PriorApplied { get; set; } = false;

        /// <summary>
        /// Whether an ArmadaDefect verdict rests on the prior alone (no rule voted ArmadaDefect): a suspected Armada
        /// defect, reported with Low confidence.
        /// </summary>
        public bool Suspected { get; set; } = false;

        private List<BlackBoxTriageVote> _Votes = new List<BlackBoxTriageVote>();
        private List<BlackBoxTriageEnum> _TiedWith = new List<BlackBoxTriageEnum>();

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxTriageVerdict()
        {
        }
    }
}
