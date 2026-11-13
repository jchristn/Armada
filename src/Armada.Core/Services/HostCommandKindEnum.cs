namespace Armada.Core.Services
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// What a host command is for, so a Harbor can present it: routine git work is collapsed in its activity log, check runs and operator commands are shown.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum HostCommandKindEnum
    {
        /// <summary>
        /// Routine git or gh work behind a mission, dock, or checkout (the default).
        /// </summary>
        [EnumMember(Value = "Routine")]
        Routine,

        /// <summary>
        /// A check run's command (build, test, or another check).
        /// </summary>
        [EnumMember(Value = "CheckRun")]
        CheckRun,

        /// <summary>
        /// A command an operator asked for (a fleet action, a Workspace command).
        /// </summary>
        [EnumMember(Value = "Command")]
        Command,

        /// <summary>
        /// A usage capture probe: the host-side helper reads account limits on the credential host. It is
        /// shown as its own activity on a Harbor and never borrows a captain mission launch slot or changes
        /// a captain's Working/Idle state.
        /// </summary>
        [EnumMember(Value = "UsageProbe")]
        UsageProbe
    }
}
