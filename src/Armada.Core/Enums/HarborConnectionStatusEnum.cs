namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Connection state of a Harbor (host runner) as seen by the Admiral.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum HarborConnectionStatusEnum
    {
        /// <summary>
        /// The Harbor has been registered but has never connected, or its state is not yet known.
        /// </summary>
        [EnumMember(Value = "Unknown")]
        Unknown,

        /// <summary>
        /// The Harbor has an open, authenticated link and recent heartbeats.
        /// </summary>
        [EnumMember(Value = "Connected")]
        Connected,

        /// <summary>
        /// The Harbor's link is open but heartbeats are late, or it is draining.
        /// </summary>
        [EnumMember(Value = "Degraded")]
        Degraded,

        /// <summary>
        /// The Harbor has no open link.
        /// </summary>
        [EnumMember(Value = "Disconnected")]
        Disconnected
    }
}
