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
        /// Tests passed, ready to land.
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
