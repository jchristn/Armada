namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Error code of a push ticket or receipt, mapped from the Expo Push Service's <c>details.error</c> code.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum PushErrorCodeEnum
    {
        /// <summary>
        /// The device can no longer receive pushes (app uninstalled or token revoked); the device is deactivated.
        /// </summary>
        [EnumMember(Value = "DeviceNotRegistered")]
        DeviceNotRegistered,

        /// <summary>
        /// The message exceeded the provider's size limit.
        /// </summary>
        [EnumMember(Value = "MessageTooBig")]
        MessageTooBig,

        /// <summary>
        /// Too many messages were sent to the device in a short time.
        /// </summary>
        [EnumMember(Value = "MessageRateExceeded")]
        MessageRateExceeded,

        /// <summary>
        /// The push credentials of the app do not match the token (Android sender id mismatch).
        /// </summary>
        [EnumMember(Value = "MismatchSenderId")]
        MismatchSenderId,

        /// <summary>
        /// The app's push credentials are invalid or missing.
        /// </summary>
        [EnumMember(Value = "InvalidCredentials")]
        InvalidCredentials,

        /// <summary>
        /// Any other error code.
        /// </summary>
        [EnumMember(Value = "Unknown")]
        Unknown
    }
}
