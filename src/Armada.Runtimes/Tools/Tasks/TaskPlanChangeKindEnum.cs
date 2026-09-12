namespace Armada.Runtimes.Tools.Tasks
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Describes what changed about a task plan when a <c>task_plan_updated</c> event is emitted.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum TaskPlanChangeKindEnum
    {
        /// <summary>
        /// A plan was established on a job that previously had none.
        /// </summary>
        [EnumMember(Value = "plan_created")]
        PlanCreated,

        /// <summary>
        /// An existing plan was replaced wholesale by a new set of tasks.
        /// </summary>
        [EnumMember(Value = "plan_replaced")]
        PlanReplaced,

        /// <summary>
        /// A single task's status changed.
        /// </summary>
        [EnumMember(Value = "task_status_changed")]
        TaskStatusChanged,

        /// <summary>
        /// A single task's note changed without a status transition.
        /// </summary>
        [EnumMember(Value = "task_note_updated")]
        TaskNoteUpdated,

        /// <summary>
        /// The plan was cleared and the job no longer has any tasks.
        /// </summary>
        [EnumMember(Value = "plan_cleared")]
        PlanCleared
    }
}
