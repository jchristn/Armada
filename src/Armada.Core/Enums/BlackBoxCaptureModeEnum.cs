namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// How a black box entry was captured.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum BlackBoxCaptureModeEnum
    {
        /// <summary>
        /// At the failure site.
        /// </summary>
        [EnumMember(Value = "Inline")]
        Inline,

        /// <summary>
        /// By the sweeper, after the fact (lower fidelity).
        /// </summary>
        [EnumMember(Value = "Sweeper")]
        Sweeper,

        /// <summary>
        /// By a backfill over historical missions (lowest fidelity).
        /// </summary>
        [EnumMember(Value = "Backfill")]
        Backfill,

        /// <summary>
        /// A value this build does not recognize (written by a newer Armada); tolerant readers map unknown values here.
        /// </summary>
        [EnumMember(Value = "Unknown")]
        Unknown
    }
}
