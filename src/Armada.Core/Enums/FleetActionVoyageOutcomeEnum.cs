namespace Armada.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Outcome of the voyage dispatched for a Mission fleet action target, as seen by the run status sync.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum FleetActionVoyageOutcomeEnum
    {
        /// <summary>
        /// The voyage is still open or in progress.
        /// </summary>
        Running,

        /// <summary>
        /// The voyage completed (its missions finished and landed).
        /// </summary>
        Succeeded,

        /// <summary>
        /// The voyage failed (a mission failed or its landing failed).
        /// </summary>
        Failed,

        /// <summary>
        /// The voyage was cancelled.
        /// </summary>
        Cancelled,

        /// <summary>
        /// The voyage no longer exists (for example it was purged).
        /// </summary>
        Missing
    }
}
