namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Suspected cause of a stage attempt that fell short (see docs/MODEL_HETEROGENEITY.md, Failure classification).
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum FailureCauseEnum
    {
        /// <summary>
        /// The attempt did not fall short; nothing to classify.
        /// </summary>
        [EnumMember(Value = "None")]
        None,

        /// <summary>
        /// The work was acceptable but the report broke the output contract (markers, sections, plan block).
        /// </summary>
        [EnumMember(Value = "Protocol")]
        Protocol,

        /// <summary>
        /// Some rules or steps were skipped (scope, boundary, commit or ASCII rules, a missed second edit).
        /// </summary>
        [EnumMember(Value = "InstructionFollowing")]
        InstructionFollowing,

        /// <summary>
        /// The tool loop failed (malformed calls, loops, iteration limits, stalls).
        /// </summary>
        [EnumMember(Value = "ToolUse")]
        ToolUse,

        /// <summary>
        /// The model lost the task or earlier reads (context exhausted, compaction).
        /// </summary>
        [EnumMember(Value = "Context")]
        Context,

        /// <summary>
        /// The model could not do the work (build or tests fail, a Judge rejects the behavior).
        /// </summary>
        [EnumMember(Value = "Capability")]
        Capability,

        /// <summary>
        /// Environment, tooling or orchestration; excluded from the model's results.
        /// </summary>
        [EnumMember(Value = "NotTheModel")]
        NotTheModel,

        /// <summary>
        /// The cause could not be determined; no rule matched.
        /// </summary>
        [EnumMember(Value = "Unknown")]
        Unknown,

        /// <summary>
        /// More than one cause contributed and they cannot be separated.
        /// </summary>
        [EnumMember(Value = "Mixed")]
        Mixed,

        /// <summary>
        /// The attempt changed files outside its owned paths or owned by a mission running alongside it. Recorded as a
        /// warning in v1.3.0; it never fails the mission.
        /// </summary>
        [EnumMember(Value = "OwnershipViolation")]
        OwnershipViolation
    }
}
