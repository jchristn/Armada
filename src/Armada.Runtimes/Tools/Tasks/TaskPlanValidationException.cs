namespace Armada.Runtimes.Tools.Tasks
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Thrown when a task plan that fails <see cref="TaskPlanValidator"/> validation is applied to a
    /// <see cref="TaskPlan"/>. Carries the ordered list of problems that made the plan invalid.
    /// </summary>
    public class TaskPlanValidationException : Exception
    {
        #region Private-Members

        private List<string> _Problems = new List<string>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="TaskPlanValidationException"/> class.
        /// </summary>
        /// <param name="message">A message describing the failure. Must not be null.</param>
        /// <param name="problems">The ordered validation problems. Null is treated as an empty list.</param>
        public TaskPlanValidationException(string message, IReadOnlyList<string>? problems)
            : base(message ?? throw new ArgumentNullException(nameof(message)))
        {
            if (problems != null) _Problems = new List<string>(problems);
        }

        #endregion

        #region Public-Members

        /// <summary>
        /// The ordered validation problems that made the plan invalid. Never null.
        /// </summary>
        public IReadOnlyList<string> Problems => _Problems;

        #endregion
    }
}
