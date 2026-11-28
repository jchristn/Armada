namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// What reading an Architect's output for a structured plan found.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ArchitectPlanParseStatusEnum
    {
        /// <summary>
        /// A valid plan was read.
        /// </summary>
        [EnumMember(Value = "Parsed")]
        Parsed,

        /// <summary>
        /// The output holds no <c>armada-plan</c> block; the caller may try a contract repair or the legacy format.
        /// </summary>
        [EnumMember(Value = "NoPlanFound")]
        NoPlanFound,

        /// <summary>
        /// The final plan block is not valid JSON for the plan model.
        /// </summary>
        [EnumMember(Value = "InvalidJson")]
        InvalidJson,

        /// <summary>
        /// The final plan block exceeds a collection limit.
        /// </summary>
        [EnumMember(Value = "TooLarge")]
        TooLarge,

        /// <summary>
        /// The final plan block was read but breaks the plan contract (a versioned plan's rules, or an unversioned plan's
        /// hierarchy or missing missions).
        /// </summary>
        [EnumMember(Value = "Rejected")]
        Rejected
    }
}
