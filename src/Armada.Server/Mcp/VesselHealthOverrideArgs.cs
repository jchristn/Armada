namespace Armada.Server.Mcp
{
    /// <summary>
    /// Arguments for the set_vessel_health_override MCP tool.
    /// </summary>
    public class VesselHealthOverrideArgs
    {
        /// <summary>
        /// Vessel identifier (vsl_ prefix).
        /// </summary>
        public string VesselId { get; set; } = string.Empty;

        /// <summary>
        /// Criterion code (GitDivergence, WorkingTree, Branches, CommitRecency, Dependencies, Vulnerabilities,
        /// TestInfrastructure, ContinuousIntegration, ArmadaReadiness, MissionOutcomes, or Overall).
        /// </summary>
        public string Criterion { get; set; } = string.Empty;

        /// <summary>
        /// Overriding status (Pass, Warn, Fail, NotApplicable, Unknown). Required unless Remove is true.
        /// </summary>
        public string? Status { get; set; } = null;

        /// <summary>
        /// Operator note, or null.
        /// </summary>
        public string? Note { get; set; } = null;

        /// <summary>
        /// When true, remove the override instead of setting it.
        /// </summary>
        public bool Remove { get; set; } = false;
    }
}
