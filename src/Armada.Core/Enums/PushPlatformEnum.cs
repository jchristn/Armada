namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Mobile platform of a push notification device.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum PushPlatformEnum
    {
        /// <summary>
        /// iOS or iPadOS (delivered by the Expo Push Service through APNs).
        /// </summary>
        [EnumMember(Value = "Ios")]
        Ios,

        /// <summary>
        /// Android (delivered by the Expo Push Service through FCM).
        /// </summary>
        [EnumMember(Value = "Android")]
        Android
    }
}
