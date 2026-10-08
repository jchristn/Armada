namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// State of a Harbor's link over one stretch of a link-health timeline.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum HarborLinkSegmentStateEnum
    {
        /// <summary>
        /// No link event is recorded for this stretch (before the Harbor first connected, or before retention kept data).
        /// </summary>
        [EnumMember(Value = "Unknown")]
        Unknown,

        /// <summary>
        /// The link was open.
        /// </summary>
        [EnumMember(Value = "Connected")]
        Connected,

        /// <summary>
        /// The link had closed and the Harbor had not dialed back in yet, within the heartbeat timeout.
        /// </summary>
        [EnumMember(Value = "Reconnecting")]
        Reconnecting,

        /// <summary>
        /// The link was down.
        /// </summary>
        [EnumMember(Value = "Down")]
        Down
    }
}
