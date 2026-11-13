namespace Armada.Core.Models
{
    /// <summary>
    /// Result of dispatching a voyage from a planning session through MCP: the voyage and how many linked objectives
    /// were linked to it. List those objectives with list_objectives or list_backlog filtered by voyageId.
    /// </summary>
    public class PlanningSessionDispatchResult
    {
        /// <summary>
        /// The dispatched voyage.
        /// </summary>
        public Voyage Voyage { get; set; } = new Voyage();

        /// <summary>
        /// Number of objectives linked to the voyage.
        /// </summary>
        public int LinkedObjectiveCount { get; set; } = 0;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public PlanningSessionDispatchResult()
        {
        }
    }
}
