namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Desktop platform a notification is delivered on.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum DesktopPlatformEnum
    {
        /// <summary>
        /// Unsupported platform: notifications are not sent.
        /// </summary>
        [EnumMember(Value = "Other")]
        Other,

        /// <summary>
        /// macOS (osascript).
        /// </summary>
        [EnumMember(Value = "MacOs")]
        MacOs,

        /// <summary>
        /// Linux (notify-send).
        /// </summary>
        [EnumMember(Value = "Linux")]
        Linux,

        /// <summary>
        /// Windows (PowerShell toast).
        /// </summary>
        [EnumMember(Value = "Windows")]
        Windows
    }
}
