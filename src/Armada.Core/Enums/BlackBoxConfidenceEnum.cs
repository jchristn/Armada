namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Confidence of a black box finding.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum BlackBoxConfidenceEnum
    {
        /// <summary>
        /// Low confidence.
        /// </summary>
        [EnumMember(Value = "Low")]
        Low,

        /// <summary>
        /// Medium confidence.
        /// </summary>
        [EnumMember(Value = "Medium")]
        Medium,

        /// <summary>
        /// High confidence.
        /// </summary>
        [EnumMember(Value = "High")]
        High,

        /// <summary>
        /// A value this build does not recognize (written by a newer Armada); tolerant readers map unknown values here.
        /// </summary>
        [EnumMember(Value = "Unknown")]
        Unknown
    }
}
