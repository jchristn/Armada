namespace Armada.Runtimes.Tools.Arguments
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Typed arguments for the plan_tasks built-in tool.
    /// </summary>
    public class PlanTasksToolArguments : IToolArguments
    {
        #region Public-Members

        /// <summary>
        /// The ordered tasks that make up the plan. Required; an empty array clears the plan.
        /// </summary>
        [JsonPropertyName("tasks")]
        public List<PlanTaskArguments?>? Tasks { get; set; } = null;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Validates that the tasks array is present and has no null entries. Ids, titles, and dependencies
        /// are checked afterwards by the task plan validator.
        /// </summary>
        /// <returns>Null when valid; otherwise the problem message.</returns>
        public string? Validate()
        {
            if (Tasks == null) return "Parameter 'tasks' is required and must be an array.";

            for (int i = 0; i < Tasks.Count; i++)
            {
                if (Tasks[i] == null) return "Task at index " + i + ": must be an object, not null.";
            }

            return null;
        }

        #endregion
    }
}
