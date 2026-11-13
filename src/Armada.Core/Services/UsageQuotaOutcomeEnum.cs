namespace Armada.Core.Services
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// What applying a quota decision did to a captain.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum UsageQuotaOutcomeEnum
    {
        /// <summary>
        /// Nothing to do: the decision was NoChange, or a release found no quota-owned hold.
        /// </summary>
        [EnumMember(Value = "Unchanged")]
        Unchanged,

        /// <summary>
        /// An idle captain was moved into a quota-owned hold.
        /// </summary>
        [EnumMember(Value = "Held")]
        Held,

        /// <summary>
        /// An existing quota-owned hold was refreshed from a newer observation.
        /// </summary>
        [EnumMember(Value = "Extended")]
        Extended,

        /// <summary>
        /// A quota-owned hold was cleared and the captain returned to Idle.
        /// </summary>
        [EnumMember(Value = "Released")]
        Released,

        /// <summary>
        /// The captain holds a manual, health or administrative quarantine that a usage observation never overwrites or clears.
        /// </summary>
        [EnumMember(Value = "SkippedForeignHold")]
        SkippedForeignHold,

        /// <summary>
        /// The observation is older than the one that set the current quota-owned hold (compare-and-set).
        /// </summary>
        [EnumMember(Value = "SkippedStale")]
        SkippedStale,

        /// <summary>
        /// The captain is working: a pre-emptive hold never interrupts a running mission.
        /// </summary>
        [EnumMember(Value = "SkippedBusy")]
        SkippedBusy,

        /// <summary>
        /// The captain does not exist.
        /// </summary>
        [EnumMember(Value = "NotFound")]
        NotFound
    }
}
