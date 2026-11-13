namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Classification section: the failure kind, the raw reason and the suspected cause.
    /// </summary>
    public class BlackBoxClassification
    {
        /// <summary>
        /// Failure kind (voyages use the root mission's kind).
        /// </summary>
        public MissionFailureKindEnum? Kind { get; set; } = null;

        /// <summary>
        /// Raw failure reason (redacted, at most 8000 characters).
        /// </summary>
        public string? RawReason { get; set; } = null;

        /// <summary>
        /// Suspected cause from the failure-cause rules.
        /// </summary>
        public FailureCauseEnum SuspectedCause { get; set; } = FailureCauseEnum.None;

        /// <summary>
        /// Rule that assigned the cause, or null.
        /// </summary>
        public string? CauseRuleId { get; set; } = null;

        /// <summary>
        /// Whether the failure is recoverable, or null when unknown.
        /// </summary>
        public bool? IsRecoverable { get; set; } = null;

        /// <summary>
        /// Whether the failure looks transient, or null when unknown.
        /// </summary>
        public bool? IsTransient { get; set; } = null;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxClassification()
        {
        }
    }
}
