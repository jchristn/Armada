namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// A transition of a Harbor's link as the Admiral saw it.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum HarborLinkEventTypeEnum
    {
        /// <summary>
        /// The Admiral accepted the Harbor's handshake.
        /// </summary>
        [EnumMember(Value = "Connected")]
        Connected,

        /// <summary>
        /// The link closed; the Harbor is expected to dial back in.
        /// </summary>
        [EnumMember(Value = "Reconnecting")]
        Reconnecting,

        /// <summary>
        /// The Harbor did not dial back in within the heartbeat timeout after its link closed, or the Admiral stopped
        /// while the link was open.
        /// </summary>
        [EnumMember(Value = "Disconnected")]
        Disconnected
    }
}
