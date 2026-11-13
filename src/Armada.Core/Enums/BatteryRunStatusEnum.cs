namespace Armada.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Lifecycle of a battery run.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum BatteryRunStatusEnum
    {
        /// <summary>
        /// Created, waiting for an evaluation slot.
        /// </summary>
        Queued,

        /// <summary>
        /// Tasks are being dispatched and graded.
        /// </summary>
        Running,

        /// <summary>
        /// Every task finished (passed, failed, could not run or pending evaluation).
        /// </summary>
        Completed,

        /// <summary>
        /// The run stopped before finishing (for example the Admiral restarted).
        /// </summary>
        Failed,

        /// <summary>
        /// Stopped before it finished (cancelled, timed out or out of budget); what finished is kept.
        /// </summary>
        Cancelled
    }
}
