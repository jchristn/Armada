namespace Armada.Core.Hosting
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Operating system family that service and startup registration targets.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum HostPlatformEnum
    {
        /// <summary>
        /// Platform not supported for registration.
        /// </summary>
        [EnumMember(Value = "Unsupported")]
        Unsupported,

        /// <summary>
        /// Microsoft Windows (Windows Service through sc.exe, Run key through reg.exe).
        /// </summary>
        [EnumMember(Value = "Windows")]
        Windows,

        /// <summary>
        /// Linux (systemd unit, XDG autostart entry).
        /// </summary>
        [EnumMember(Value = "Linux")]
        Linux,

        /// <summary>
        /// macOS (launchd agent plist).
        /// </summary>
        [EnumMember(Value = "MacOS")]
        MacOS
    }
}
