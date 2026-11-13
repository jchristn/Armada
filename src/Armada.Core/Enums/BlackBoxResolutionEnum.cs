namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Resolution of a black box entry.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum BlackBoxResolutionEnum
    {
        /// <summary>
        /// Not resolved.
        /// </summary>
        [EnumMember(Value = "Open")]
        Open,

        /// <summary>
        /// Resolved.
        /// </summary>
        [EnumMember(Value = "Resolved")]
        Resolved,

        /// <summary>
        /// A duplicate of another entry.
        /// </summary>
        [EnumMember(Value = "Duplicate")]
        Duplicate,

        /// <summary>
        /// Will not be fixed.
        /// </summary>
        [EnumMember(Value = "WontFix")]
        WontFix,

        /// <summary>
        /// Fixed by a change to Armada.
        /// </summary>
        [EnumMember(Value = "ArmadaFixed")]
        ArmadaFixed,

        /// <summary>
        /// A value this build does not recognize (written by a newer Armada); tolerant readers map unknown values here.
        /// </summary>
        [EnumMember(Value = "Unknown")]
        Unknown
    }
}
