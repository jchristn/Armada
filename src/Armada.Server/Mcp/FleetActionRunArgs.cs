namespace Armada.Server.Mcp
{
    using System.Collections.Generic;

    /// <summary>
    /// MCP tool arguments for starting a fleet action run. Supply actionId to run a saved action, or the inline
    /// definition fields (name, kind, commandText or promptTemplate, ...) for an ad hoc run.
    /// </summary>
    public class FleetActionRunArgs : FleetActionUpsertArgs
    {
        /// <summary>Approval choices for this run's tool calls and human mission reviews.</summary>
        public Armada.Core.Models.RunApprovalOptions? ApprovalOptions { get; set; } = null;

        /// <summary>
        /// Target vessel IDs (vsl_ prefix) in the caller's tenant.
        /// </summary>
        public List<string>? VesselIds { get; set; }

        /// <summary>
        /// Optional concurrency for this run (1 to 32).
        /// </summary>
        public int? Concurrency { get; set; }
        /// <summary>Runtime input values.</summary>
        public Dictionary<string, string>? InputValues { get; set; }

        /// <summary>
        /// Follow-up run: target the vessels of this earlier run whose status is in FromTargetStatuses (instead of
        /// vesselIds).
        /// </summary>
        public string? FromRunId { get; set; }

        /// <summary>
        /// Target statuses selected from FromRunId (Findings, Failed, Succeeded, Skipped, TimedOut, Cancelled). Defaults
        /// to Findings and Failed.
        /// </summary>
        public List<string>? FromTargetStatuses { get; set; }
    }
}
