namespace Armada.Runtimes.Tools.Tasks
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Lifecycle status of a single task within a job's task plan.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum AgentTaskStatusEnum
    {
        /// <summary>
        /// The task has not been started. Its dependencies may or may not be satisfied.
        /// </summary>
        [EnumMember(Value = "pending")]
        Pending,

        /// <summary>
        /// The task is actively being worked. In tracking mode at most one task should hold this status.
        /// </summary>
        [EnumMember(Value = "in_progress")]
        InProgress,

        /// <summary>
        /// The task finished successfully.
        /// </summary>
        [EnumMember(Value = "completed")]
        Completed,

        /// <summary>
        /// The task was attempted and failed. A failed task always carries an explanatory note.
        /// </summary>
        [EnumMember(Value = "failed")]
        Failed,

        /// <summary>
        /// The task was intentionally not done — no longer needed or superseded by a re-plan.
        /// </summary>
        [EnumMember(Value = "skipped")]
        Skipped,

        /// <summary>
        /// The task cannot proceed yet because it is waiting on a dependency or an external condition
        /// the model has flagged. Distinct from <see cref="Pending"/>, which is simply not-yet-started.
        /// </summary>
        [EnumMember(Value = "blocked")]
        Blocked
    }
}
