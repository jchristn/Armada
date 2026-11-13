namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// How the Admiral settled a captain that was unreachable (its Harbor lost the link) or whose stop was not confirmed,
    /// recorded in a <c>captain.reconciled</c> event.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum CaptainReconcileOutcomeEnum
    {
        /// <summary>
        /// The captain was still working: monitoring resumed (and a pending stop was cancelled) with the stall clock
        /// restarted at the reconnect.
        /// </summary>
        [EnumMember(Value = "Resumed")]
        Resumed,

        /// <summary>
        /// The captain was stuck: the pending stop was delivered, the Harbor confirmed it, and recovery continued.
        /// </summary>
        [EnumMember(Value = "Stopped")]
        Stopped,

        /// <summary>
        /// The captain's process had already exited; its exit was applied through the normal exit path.
        /// </summary>
        [EnumMember(Value = "Exited")]
        Exited,

        /// <summary>
        /// The captain moved on (another mission or a relaunched process), so the episode no longer applied.
        /// </summary>
        [EnumMember(Value = "Superseded")]
        Superseded
    }
}
