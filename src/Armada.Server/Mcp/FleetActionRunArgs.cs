namespace Armada.Server.Mcp
{
    using System.Collections.Generic;

    /// <summary>
    /// MCP tool arguments for starting a fleet action run. Supply actionId to run a saved action, or the inline
    /// definition fields (name, kind, commandText or promptTemplate, ...) for an ad hoc run.
    /// </summary>
    public class FleetActionRunArgs : FleetActionUpsertArgs
    {
        /// <summary>
        /// Target vessel IDs (vsl_ prefix) in the caller's tenant.
        /// </summary>
        public List<string>? VesselIds { get; set; }

        /// <summary>
        /// Optional concurrency for this run (1 to 32).
        /// </summary>
        public int? Concurrency { get; set; }
    }
}
