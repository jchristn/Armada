namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Severity or risk level.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum BlackBoxSeverityEnum
    {
        /// <summary>
        /// Low.
        /// </summary>
        [EnumMember(Value = "Low")]
        Low,

        /// <summary>
        /// Medium.
        /// </summary>
        [EnumMember(Value = "Medium")]
        Medium,

        /// <summary>
        /// High.
        /// </summary>
        [EnumMember(Value = "High")]
        High,

        /// <summary>
        /// Critical.
        /// </summary>
        [EnumMember(Value = "Critical")]
        Critical,

        /// <summary>
        /// A value this build does not recognize (written by a newer Armada); tolerant readers map unknown values here.
        /// </summary>
        [EnumMember(Value = "Unknown")]
        Unknown
    }
}
