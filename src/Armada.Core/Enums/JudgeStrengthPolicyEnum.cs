namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// The Judge strength rule of a pipeline (RS-8.7): a Judge must be at least as strong as the stages it reviews.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum JudgeStrengthPolicyEnum
    {
        /// <summary>
        /// No rule: any eligible captain may judge (the 1.4.0 behavior, and what pipelines stored before 1.5.0 read as).
        /// </summary>
        [EnumMember(Value = "Off")]
        Off,

        /// <summary>
        /// A Judge whose tier is below the highest tier among the stages it reviews is not eligible. When no captain
        /// meets the rule, the Judge waits with the judge_strength blocker. The default for new pipelines.
        /// </summary>
        [EnumMember(Value = "Wait")]
        Wait,

        /// <summary>
        /// Prefer a captain that meets the rule; when none is available, run the best available Judge and record a
        /// judge strength exception event on the mission (gate G8).
        /// </summary>
        [EnumMember(Value = "ProceedAndFlag")]
        ProceedAndFlag
    }
}
