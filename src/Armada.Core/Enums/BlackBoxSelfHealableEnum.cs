namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Whether Armada could have healed a black box failure by itself (resilience analysis).
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum BlackBoxSelfHealableEnum
    {
        /// <summary>
        /// Armada could have recovered automatically (retry, recreate, rebase, repair) without a person.
        /// </summary>
        [EnumMember(Value = "Yes")]
        Yes,

        /// <summary>
        /// Recovery needs a person or a change outside Armada.
        /// </summary>
        [EnumMember(Value = "No")]
        No,

        /// <summary>
        /// Not decided, or a value this build does not recognize (written by a newer Armada); tolerant readers map unknown values here.
        /// </summary>
        [EnumMember(Value = "Unknown")]
        Unknown
    }
}
