namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// How a captain participates in account usage capture. Disabled always wins: a captain in Disabled
    /// mode is opted out of limit capture and account membership regardless of <c>UsageCaptureEnabled</c>.
    /// Auto and Custom both additionally require <c>UsageCaptureEnabled</c> to be true. Auto uses only
    /// verified read-only sources; Custom runs the configured local executable (UsageCaptureCommand).
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum UsageCaptureModeEnum
    {
        /// <summary>
        /// Capture through the runtime's verified read-only account sources only.
        /// </summary>
        [EnumMember(Value = "Auto")]
        Auto,

        /// <summary>
        /// Opt the captain out of limit capture and account membership entirely.
        /// </summary>
        [EnumMember(Value = "Disabled")]
        Disabled,

        /// <summary>
        /// Capture through the configured administrator-chosen local executable contract.
        /// </summary>
        [EnumMember(Value = "Custom")]
        Custom
    }
}