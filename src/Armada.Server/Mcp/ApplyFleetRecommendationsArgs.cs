namespace Armada.Server.Mcp
{
    using System.Collections.Generic;
    using Armada.Core.Models;

    /// <summary>
    /// MCP tool arguments for applying fleet recommendations to an import batch.
    /// </summary>
    public class ApplyFleetRecommendationsArgs
    {
        /// <summary>
        /// Batch ID (vib_ prefix).
        /// </summary>
        public string BatchId { get; set; } = "";

        /// <summary>
        /// Fleets to apply. Omit to apply the batch's stored recommendations unchanged.
        /// </summary>
        public List<FleetRecommendationApplyFleet>? Fleets { get; set; }
    }
}
