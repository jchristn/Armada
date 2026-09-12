namespace Armada.Runtimes.Tools.Tasks
{
    using System.Collections.Generic;
    using Armada.Runtimes.Tools.Tasks;

    /// <summary>
    /// A compact tally of a task plan's tasks by status, attached to a completed run so a caller can see
    /// how the plan resolved without replaying every event.
    /// </summary>
    public class TaskPlanSummary
    {
        #region Public-Members

        /// <summary>
        /// The total number of tasks in the plan.
        /// </summary>
        public int Total { get; set; }

        /// <summary>
        /// The number of tasks that completed successfully.
        /// </summary>
        public int Completed { get; set; }

        /// <summary>
        /// The number of tasks still pending at run end.
        /// </summary>
        public int Pending { get; set; }

        /// <summary>
        /// The number of tasks in progress at run end.
        /// </summary>
        public int InProgress { get; set; }

        /// <summary>
        /// The number of tasks that failed.
        /// </summary>
        public int Failed { get; set; }

        /// <summary>
        /// The number of tasks that were skipped.
        /// </summary>
        public int Skipped { get; set; }

        /// <summary>
        /// The number of tasks that were blocked at run end.
        /// </summary>
        public int Blocked { get; set; }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Builds a summary by tallying the given tasks by status.
        /// </summary>
        /// <param name="tasks">The tasks to tally. Null is treated as an empty plan.</param>
        /// <returns>A populated <see cref="TaskPlanSummary"/>.</returns>
        public static TaskPlanSummary FromTasks(IReadOnlyList<AgentTask>? tasks)
        {
            TaskPlanSummary summary = new TaskPlanSummary();
            if (tasks == null) return summary;

            summary.Total = tasks.Count;
            foreach (AgentTask task in tasks)
            {
                switch (task.Status)
                {
                    case AgentTaskStatusEnum.Completed: summary.Completed++; break;
                    case AgentTaskStatusEnum.Pending: summary.Pending++; break;
                    case AgentTaskStatusEnum.InProgress: summary.InProgress++; break;
                    case AgentTaskStatusEnum.Failed: summary.Failed++; break;
                    case AgentTaskStatusEnum.Skipped: summary.Skipped++; break;
                    case AgentTaskStatusEnum.Blocked: summary.Blocked++; break;
                }
            }

            return summary;
        }

        #endregion
    }
}
