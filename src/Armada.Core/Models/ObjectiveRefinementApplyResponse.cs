namespace Armada.Core.Models
{
    /// <summary>
    /// Response returned after applying a refinement summary back to an objective.
    /// </summary>
    public class ObjectiveRefinementApplyResponse
    {
        /// <summary>
        /// Gets or sets the summary.
        /// </summary>
        public ObjectiveRefinementSummaryResponse Summary { get; set; } = new ObjectiveRefinementSummaryResponse();
        /// <summary>
        /// Gets or sets the objective.
        /// </summary>
        public Objective Objective { get; set; } = new Objective();
        /// <summary>
        /// Gets or sets the refinement session after the apply (Stopped when the apply ended it), or null.
        /// </summary>
        public ObjectiveRefinementSession? Session { get; set; } = null;
    }
}
