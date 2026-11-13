namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Triage verdict: whose problem the failure most likely is.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum BlackBoxTriageEnum
    {
        /// <summary>
        /// Not determined, or a tie between candidates.
        /// </summary>
        [EnumMember(Value = "Unknown")]
        Unknown,

        /// <summary>
        /// The user's configuration.
        /// </summary>
        [EnumMember(Value = "UserConfig")]
        UserConfig,

        /// <summary>
        /// The mission brief or dependency ordering.
        /// </summary>
        [EnumMember(Value = "MissionBrief")]
        MissionBrief,

        /// <summary>
        /// A transient condition; retrying is likely to work.
        /// </summary>
        [EnumMember(Value = "Transient")]
        Transient,

        /// <summary>
        /// A defect in Armada itself.
        /// </summary>
        [EnumMember(Value = "ArmadaDefect")]
        ArmadaDefect,

        /// <summary>
        /// Something outside Armada (an operator action, a provider, the network).
        /// </summary>
        [EnumMember(Value = "External")]
        External
    }
}
