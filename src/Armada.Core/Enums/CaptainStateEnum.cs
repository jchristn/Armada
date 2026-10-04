namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// State of a captain agent.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum CaptainStateEnum
    {
        /// <summary>
        /// Captain is idle and available for assignment.
        /// </summary>
        [EnumMember(Value = "Idle")]
        Idle,

        /// <summary>
        /// Captain is actively working on a mission.
        /// </summary>
        [EnumMember(Value = "Working")]
        Working,

        /// <summary>
        /// Captain is reserved for a planning session.
        /// </summary>
        [EnumMember(Value = "Planning")]
        Planning,

        /// <summary>
        /// Captain is reserved for a backlog refinement session.
        /// </summary>
        [EnumMember(Value = "Refining")]
        Refining,

        /// <summary>
        /// Captain process appears stalled.
        /// </summary>
        [EnumMember(Value = "Stalled")]
        Stalled,

        /// <summary>
        /// Captain is in the process of stopping.
        /// </summary>
        [EnumMember(Value = "Stopping")]
        Stopping,

        /// <summary>
        /// Captain is quarantined (provider usage-limit / auth failure or crash-loop) and excluded from
        /// dispatch selection until its quarantine expires.
        /// </summary>
        [EnumMember(Value = "Quarantined")]
        Quarantined,

        /// <summary>
        /// Captain is reserved for a one-off analysis job outside any mission or dock (for example recommending fleets
        /// for imported repositories). It returns to Idle when the job finishes, fails, or is cancelled.
        /// </summary>
        [EnumMember(Value = "Analyzing")]
        Analyzing
    }
}
