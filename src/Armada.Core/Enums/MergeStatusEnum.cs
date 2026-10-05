namespace Armada.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Status of a merge queue entry.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum MergeStatusEnum
    {
        /// <summary>
        /// Queued for merge, waiting to be picked up.
        /// </summary>
        Queued,

        /// <summary>
        /// Currently being tested (merged into integration branch, tests running).
        /// </summary>
        Testing,

        /// <summary>
        /// Reserved: tests passed, ready to land. The merge queue lands an entry as soon as its tests pass, so no entry is
        /// currently left in this status; it is accepted as a filter value and counted as active (non-terminal). Kept for
        /// API compatibility and a future hold-before-landing step.
        /// </summary>
        Passed,

        /// <summary>
        /// Tests failed.
        /// </summary>
        Failed,

        /// <summary>
        /// Successfully merged into the target branch.
        /// </summary>
        Landed,

        /// <summary>
        /// Removed from the queue (manually or due to conflict).
        /// </summary>
        Cancelled
    }
}
