namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Type of signal between agents.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum SignalTypeEnum
    {
        /// <summary>
        /// Mission assignment notification.
        /// </summary>
        [EnumMember(Value = "Assignment")]
        Assignment,

        /// <summary>
        /// Progress update from a captain.
        /// </summary>
        [EnumMember(Value = "Progress")]
        Progress,

        /// <summary>
        /// Mission completion notification.
        /// </summary>
        [EnumMember(Value = "Completion")]
        Completion,

        /// <summary>
        /// Error notification.
        /// </summary>
        [EnumMember(Value = "Error")]
        Error,

        /// <summary>
        /// Heartbeat signal.
        /// </summary>
        [EnumMember(Value = "Heartbeat")]
        Heartbeat,

        /// <summary>
        /// Ephemeral nudge message.
        /// </summary>
        [EnumMember(Value = "Nudge")]
        Nudge,

        /// <summary>
        /// Persistent mail message.
        /// </summary>
        [EnumMember(Value = "Mail")]
        Mail
    }
}
