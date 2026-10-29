namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Which host a black box failure happened on.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum BlackBoxHostKindEnum
    {
        /// <summary>
        /// The Admiral host.
        /// </summary>
        [EnumMember(Value = "Admiral")]
        Admiral,

        /// <summary>
        /// A Harbor host.
        /// </summary>
        [EnumMember(Value = "Harbor")]
        Harbor,

        /// <summary>
        /// Not known.
        /// </summary>
        [EnumMember(Value = "Unknown")]
        Unknown
    }
}
