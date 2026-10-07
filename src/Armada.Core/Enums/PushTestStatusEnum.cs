namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Outcome of a test push to one device.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum PushTestStatusEnum
    {
        /// <summary>
        /// The Expo Push Service accepted the message (a ticket was issued). Final delivery is reported by a receipt
        /// later.
        /// </summary>
        [EnumMember(Value = "Sent")]
        Sent,

        /// <summary>
        /// Push delivery is disabled in the server settings (Push.Enabled is false).
        /// </summary>
        [EnumMember(Value = "Disabled")]
        Disabled,

        /// <summary>
        /// The device is inactive; register it again from the app to reactivate it.
        /// </summary>
        [EnumMember(Value = "DeviceInactive")]
        DeviceInactive,

        /// <summary>
        /// The Expo Push Service reported the device as not registered; the device was deactivated.
        /// </summary>
        [EnumMember(Value = "DeviceNotRegistered")]
        DeviceNotRegistered,

        /// <summary>
        /// The per-user push rate limit was reached; try again in a minute.
        /// </summary>
        [EnumMember(Value = "RateLimited")]
        RateLimited,

        /// <summary>
        /// The Expo Push Service rejected the message or could not be reached.
        /// </summary>
        [EnumMember(Value = "Failed")]
        Failed
    }
}
