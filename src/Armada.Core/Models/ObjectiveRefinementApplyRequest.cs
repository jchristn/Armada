namespace Armada.Core.Models
{
    /// <summary>
    /// Request payload for applying a refinement summary back to an objective.
    /// </summary>
    public class ObjectiveRefinementApplyRequest
    {
        /// <summary>
        /// Gets or sets the message identifier.
        /// </summary>
        public string? MessageId { get; set; } = null;
        /// <summary>
        /// Gets or sets a value indicating whether the selected message should be marked as selected.
        /// </summary>
        public bool MarkMessageSelected { get; set; } = true;
        /// <summary>
        /// Gets or sets a value indicating whether the objective backlog state should be promoted.
        /// </summary>
        public bool PromoteBacklogState { get; set; } = true;
        /// <summary>
        /// Gets or sets a value indicating whether the refinement session ends after the summary is applied (default
        /// true), which releases its captain for missions. Set false to keep refining in the same session.
        /// </summary>
        public bool EndSession { get; set; } = true;
    }
}
