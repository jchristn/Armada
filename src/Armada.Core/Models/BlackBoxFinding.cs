namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// One why finding.
    /// </summary>
    public class BlackBoxFinding
    {
        /// <summary>
        /// Rule that produced it (for example BBX-LAND-003).
        /// </summary>
        public string RuleId { get; set; } = "";

        /// <summary>
        /// Statement.
        /// </summary>
        public string Statement { get; set; } = "";

        /// <summary>
        /// Confidence.
        /// </summary>
        public BlackBoxConfidenceEnum Confidence { get; set; } = BlackBoxConfidenceEnum.Medium;

        /// <summary>
        /// References to evidence (section paths or object names).
        /// </summary>
        [CollectionBound(BlackBoxLimits.MaxEvidenceRefs, Reason = "Capped by BlackBoxRecordBounder")]
        public List<string> EvidenceRefs
        {
            get => _EvidenceRefs;
            set => _EvidenceRefs = value ?? new List<string>();
        }

        private List<string> _EvidenceRefs = new List<string>();

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxFinding()
        {
        }
    }
}
