namespace Armada.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Lifecycle status of a fleet action run.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum FleetActionRunStatusEnum
    {
        /// <summary>
        /// The run is persisted and waiting to start.
        /// </summary>
        Pending,

        /// <summary>
        /// The run is executing its targets.
        /// </summary>
        Running,

        /// <summary>
        /// Every target finished successfully or was skipped.
        /// </summary>
        Completed,

        /// <summary>
        /// The run finished but at least one target failed or timed out.
        /// </summary>
        CompletedWithFailures,

        /// <summary>
        /// The run was cancelled before every target finished.
        /// </summary>
        Cancelled,

        /// <summary>
        /// The run as a whole failed before completing.
        /// </summary>
        Failed
    }
}
