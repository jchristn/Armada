namespace Armada.Server.Mcp
{
    using System.Collections.Generic;

    /// <summary>
    /// Arguments for the evaluate_vessel_health MCP tool.
    /// </summary>
    public class EvaluateVesselHealthArgs
    {
        /// <summary>
        /// Vessel identifiers to evaluate, or null for all active vessels (or the fleet's).
        /// </summary>
        public List<string>? VesselIds { get; set; } = null;

        /// <summary>
        /// Fleet whose active vessels to evaluate, or null.
        /// </summary>
        public string? FleetId { get; set; } = null;

        /// <summary>
        /// Whether to force dependency and vulnerability checks (null means true).
        /// </summary>
        public bool? Force { get; set; } = null;
    }
}
