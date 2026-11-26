namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// What <see cref="Armada.Core.Services.RecoveryPolicyEngine"/> chooses to do about a failure (RS-5.10).
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum RecoveryActionEnum
    {
        /// <summary>
        /// Send the mission back to Pending for a fresh dispatch (interruptions and no-op completions).
        /// </summary>
        [EnumMember(Value = "Redispatch")]
        Redispatch,

        /// <summary>
        /// Relaunch the same captain in the same dock, with a note about the earlier attempt.
        /// </summary>
        [EnumMember(Value = "Resume")]
        Resume,

        /// <summary>
        /// Rerun the stage with a note asking for output in the contract's format.
        /// </summary>
        [EnumMember(Value = "Repair")]
        Repair,

        /// <summary>
        /// Schedule another Worker round with the reviewer's findings.
        /// </summary>
        [EnumMember(Value = "Rework")]
        Rework,

        /// <summary>
        /// Hand the work, on the same branch, to a stronger captain on the escalation ladder.
        /// </summary>
        [EnumMember(Value = "Escalate")]
        Escalate,

        /// <summary>
        /// Dispatch a rescue mission for the failed mission.
        /// </summary>
        [EnumMember(Value = "Rescue")]
        Rescue,

        /// <summary>
        /// Do nothing now; a recovery attempt is already in flight.
        /// </summary>
        [EnumMember(Value = "Wait")]
        Wait,

        /// <summary>
        /// Stop recovering: the mission fails, or the incident is left for a human.
        /// </summary>
        [EnumMember(Value = "Fail")]
        Fail
    }
}
