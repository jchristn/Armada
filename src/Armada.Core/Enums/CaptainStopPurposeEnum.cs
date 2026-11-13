namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Why the health check asked for a captain's process to stop, recorded with a stop that was not confirmed so the
    /// same step continues once the stop is confirmed.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum CaptainStopPurposeEnum
    {
        /// <summary>
        /// A stall recovery attempt: stop the stalled process, then relaunch the captain in the same dock.
        /// </summary>
        [EnumMember(Value = "Recovery")]
        Recovery,

        /// <summary>
        /// Stall recovery is exhausted: stop the process, then fail the mission.
        /// </summary>
        [EnumMember(Value = "RecoveryExhausted")]
        RecoveryExhausted,

        /// <summary>
        /// The mission ran past MaxMissionRuntimeMinutes: stop the process, then fail the mission.
        /// </summary>
        [EnumMember(Value = "MaxRuntime")]
        MaxRuntime
    }
}
