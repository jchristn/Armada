namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// How Armada deterministically repaired a stage whose work was acceptable but whose report broke the output contract.
    /// A repair never creates a verdict, test evidence or work the stage did not produce.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ContractRepairKindEnum
    {
        /// <summary>
        /// No repair was needed.
        /// </summary>
        [EnumMember(Value = "None")]
        None,

        /// <summary>
        /// A Worker or Test Engineer wrote no completion line, but its dock branch holds new work, so the work counts as produced.
        /// </summary>
        [EnumMember(Value = "CompletionLineMissing")]
        CompletionLineMissing,

        /// <summary>
        /// A Worker or Test Engineer wrote a completion line in a non-canonical form, and its dock branch holds new work.
        /// </summary>
        [EnumMember(Value = "CompletionLineMalformed")]
        CompletionLineMalformed,

        /// <summary>
        /// The captain left its work uncommitted and Armada's leftover-work commit put it on the branch.
        /// </summary>
        [EnumMember(Value = "CommitMissingRecoveredFromRepo")]
        CommitMissingRecoveredFromRepo,

        /// <summary>
        /// A Judge wrote exactly one PASS, FAIL or NEEDS_REVISION verdict in a nearby non-canonical form.
        /// </summary>
        [EnumMember(Value = "VerdictNonCanonical")]
        VerdictNonCanonical,

        /// <summary>
        /// An Architect wrote exactly one valid plan, but not in a closed armada-plan fence.
        /// </summary>
        [EnumMember(Value = "PlanBlockNonCanonical")]
        PlanBlockNonCanonical,

        /// <summary>
        /// A format-only repair turn restated the stage's own output in the contract's format, without tools and without
        /// redoing the work; the restatement is appended below the original output (RS-6.4).
        /// </summary>
        [EnumMember(Value = "FormatRepairTurn")]
        FormatRepairTurn
    }
}
