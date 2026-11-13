namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Format of a maintainer bundle.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum BlackBoxBundleFormatEnum
    {
        /// <summary>
        /// Markdown.
        /// </summary>
        [EnumMember(Value = "Markdown")]
        Markdown,

        /// <summary>
        /// JSON.
        /// </summary>
        [EnumMember(Value = "Json")]
        Json,

        /// <summary>
        /// A value this build does not recognize (written by a newer Armada); tolerant readers map unknown values here.
        /// </summary>
        [EnumMember(Value = "Unknown")]
        Unknown
    }
}
