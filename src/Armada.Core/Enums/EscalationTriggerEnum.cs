namespace Armada.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Conditions that can trigger an escalation.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum EscalationTriggerEnum
    {
        /// <summary>
        /// A captain has been stalled for longer than the threshold.
        /// </summary>
        CaptainStalled,

        /// <summary>
        /// A mission has been in progress for longer than the threshold.
        /// </summary>
        MissionOverdue,

        /// <summary>
        /// A mission has failed.
        /// </summary>
        MissionFailed,

        /// <summary>
        /// Recovery attempts exhausted for a captain.
        /// </summary>
        RecoveryExhausted,

        /// <summary>
        /// All captains are busy (no idle captains available).
        /// </summary>
        PoolExhausted
    }
}
