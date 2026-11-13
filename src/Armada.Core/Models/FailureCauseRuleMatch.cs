namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// What a failure-cause rule matches. Every criterion given must match (AND); within a list any value matches (OR). A rule with no criteria matches nothing.
    /// </summary>
    public class FailureCauseRuleMatch
    {
        #region Public-Members

        /// <summary>
        /// Persona names (case-insensitive).
        /// </summary>
        [CollectionBound(CollectionLimits.MaxConfigItems)]
        public List<string> Personas { get; set; } = new List<string>();

        /// <summary>
        /// Runtimes.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxConfigItems)]
        public List<AgentRuntimeEnum> Runtimes { get; set; } = new List<AgentRuntimeEnum>();

        /// <summary>
        /// Observed mission failure kinds.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxConfigItems)]
        public List<MissionFailureKindEnum> FailureKinds { get; set; } = new List<MissionFailureKindEnum>();

        /// <summary>
        /// Runtime exit classifications.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxConfigItems)]
        public List<RuntimeFailureKindEnum> RuntimeFailureKinds { get; set; } = new List<RuntimeFailureKindEnum>();

        /// <summary>
        /// Stage check statuses.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxConfigItems)]
        public List<StageCheckStatusEnum> StageCheckStatuses { get; set; } = new List<StageCheckStatusEnum>();

        /// <summary>
        /// Stage check reasons.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxConfigItems)]
        public List<StageCheckReasonEnum> StageCheckReasons { get; set; } = new List<StageCheckReasonEnum>();

        /// <summary>
        /// Dispositions.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxConfigItems)]
        public List<StageDispositionEnum> Dispositions { get; set; } = new List<StageDispositionEnum>();

        /// <summary>
        /// Regular expression (case-insensitive) matched against the failure reason, or null.
        /// </summary>
        public string? TextPattern { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public FailureCauseRuleMatch()
        {
        }

        #endregion
    }
}
