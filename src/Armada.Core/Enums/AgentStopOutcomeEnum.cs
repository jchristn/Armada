namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Result of asking a runtime to stop one launched process.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum AgentStopOutcomeEnum
    {
        /// <summary>
        /// The recorded process was stopped.
        /// </summary>
        [EnumMember(Value = "Stopped")]
        Stopped,

        /// <summary>
        /// No process with that id was running.
        /// </summary>
        [EnumMember(Value = "NotRunning")]
        NotRunning,

        /// <summary>
        /// The process was left running because it was not the recorded launch.
        /// </summary>
        [EnumMember(Value = "Refused")]
        Refused
    }
}
