namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// What dispatch decided for a Worker mission under a Worker eligibility envelope.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum WorkerEnvelopeOutcomeEnum
    {
        /// <summary>
        /// Normal routing assigned an idle captain that meets the envelope.
        /// </summary>
        [EnumMember(Value = "Assigned")]
        Assigned,

        /// <summary>
        /// No idle captain normal routing would use met the envelope, so a stronger idle captain that meets it and may
        /// run the Worker persona was assigned instead of waiting.
        /// </summary>
        [EnumMember(Value = "Escalated")]
        Escalated,

        /// <summary>
        /// Captains that meet the envelope exist but none is idle; the mission stays Pending.
        /// </summary>
        [EnumMember(Value = "Waiting")]
        Waiting,

        /// <summary>
        /// No captain at all meets the envelope (or may run the Worker persona); the mission stays Pending until a
        /// captain or model profile changes.
        /// </summary>
        [EnumMember(Value = "NoEligibleCaptain")]
        NoEligibleCaptain,

        /// <summary>
        /// The Architect's envelope for the mission loosened the vessel default (or was invalid) and was clamped to it.
        /// </summary>
        [EnumMember(Value = "Clamped")]
        Clamped
    }
}
