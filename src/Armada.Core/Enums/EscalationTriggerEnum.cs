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
        /// A mission has failed, keyed by mission id. Fires on every terminal failure.
        /// </summary>
        MissionFailed,

        /// <summary>
        /// A failed mission used up its autonomous rescue budget (MaxMissionRecoveryAttempts) without a rescue landing,
        /// keyed by the original mission id. Fires once per recovery incident.
        /// </summary>
        RecoveryExhausted,

        /// <summary>
        /// All captains are busy (no idle captains available).
        /// </summary>
        PoolExhausted
    }
}
