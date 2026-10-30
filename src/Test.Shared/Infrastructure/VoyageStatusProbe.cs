namespace Test.Shared.Infrastructure
{
    using Armada.Core.Models;

    /// <summary>
    /// The voyage_status MCP reply as read by tests: the (summary or full) voyage and its mission count.
    /// </summary>
    public class VoyageStatusProbe
    {
        /// <summary>
        /// The voyage (the summary carries a subset of the fields).
        /// </summary>
        public Voyage? Voyage { get; set; } = null;

        /// <summary>
        /// Missions in the voyage.
        /// </summary>
        public int TotalMissions { get; set; } = 0;
    }
}
