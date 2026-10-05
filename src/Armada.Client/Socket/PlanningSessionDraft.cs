namespace Armada.Client.Socket
{
    using System;

    /// <summary>
    /// The dispatch draft carried by a <c>planning-session.summary.created</c> event.
    /// </summary>
    public class PlanningSessionDraft
    {
        #region Public-Members

        /// <summary>
        /// Draft voyage title.
        /// </summary>
        public string? Title { get; set; } = null;

        /// <summary>
        /// Draft mission description.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Summarization method.
        /// </summary>
        public string? Method { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public PlanningSessionDraft()
        {
        }

        #endregion
    }
}
