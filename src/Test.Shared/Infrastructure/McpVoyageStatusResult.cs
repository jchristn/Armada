namespace Test.Shared.Infrastructure
{
    using System.Collections.Generic;
    using Armada.Core.Models;

    /// <summary>
    /// Result of the MCP voyage_status and cancel_voyage tools: the voyage, mission counts, and (opt-in) mission objects.
    /// </summary>
    public class McpVoyageStatusResult
    {
        #region Public-Members

        /// <summary>
        /// Voyage (a projection in summary mode), or null.
        /// </summary>
        public Voyage? Voyage { get; set; } = null;

        /// <summary>
        /// Number of missions in the voyage, or null when not reported.
        /// </summary>
        public int? TotalMissions { get; set; } = null;

        /// <summary>
        /// Mission counts keyed by status name (summary mode), or null.
        /// </summary>
        public Dictionary<string, int>? MissionCountsByStatus { get; set; } = null;

        /// <summary>
        /// Mission objects (when requested), or null.
        /// </summary>
        public List<Mission>? Missions { get; set; } = null;

        /// <summary>
        /// Missions cancelled (cancel_voyage), or null.
        /// </summary>
        public int? CancelledMissions { get; set; } = null;

        #endregion
    }
}
