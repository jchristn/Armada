namespace Armada.Helm.Commands
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Decides the missions for <c>armada go</c>. Tasks come only from explicit <c>--task</c> options; the prompt is
    /// never split on list numbering or punctuation, so a prompt such as "fix a; b" or "update v1. then v2." stays one
    /// mission.
    /// </summary>
    public static class GoTaskList
    {
        #region Public-Methods

        /// <summary>
        /// Build the mission list.
        /// </summary>
        /// <param name="prompt">Prompt argument.</param>
        /// <param name="tasks">Values of repeated <c>--task</c> options, or null.</param>
        /// <returns>The non-blank --task values in order when any are given; otherwise the prompt as a single task;
        /// empty when both are blank.</returns>
        public static List<string> Build(string? prompt, IEnumerable<string>? tasks)
        {
            List<string> result = new List<string>();
            if (tasks != null)
            {
                foreach (string task in tasks)
                {
                    if (!String.IsNullOrWhiteSpace(task)) result.Add(task.Trim());
                }
            }

            if (result.Count > 0) return result;
            if (!String.IsNullOrWhiteSpace(prompt)) result.Add(prompt.Trim());
            return result;
        }

        #endregion
    }
}
