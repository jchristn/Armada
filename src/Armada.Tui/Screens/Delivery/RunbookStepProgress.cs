namespace Armada.Tui.Screens.Delivery
{
    using System;
    using Armada.Core.Models;

    /// <summary>
    /// One step of a runbook execution's checklist: the step, whether it is complete, and its note.
    /// </summary>
    public class RunbookStepProgress
    {
        #region Public-Members

        /// <summary>
        /// The runbook step.
        /// </summary>
        public RunbookStep Step { get; }

        /// <summary>
        /// Completed.
        /// </summary>
        public bool Done { get; set; } = false;

        /// <summary>
        /// Step note.
        /// </summary>
        public string Note { get; set; } = "";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="step">Step.</param>
        /// <param name="done">Completed.</param>
        /// <param name="note">Note.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="step"/> is null.</exception>
        public RunbookStepProgress(RunbookStep step, bool done, string? note)
        {
            Step = step ?? throw new ArgumentNullException(nameof(step));
            Done = done;
            Note = note ?? "";
        }

        #endregion
    }
}
