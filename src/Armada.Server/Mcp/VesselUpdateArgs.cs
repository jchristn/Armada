namespace Armada.Server.Mcp
{
    /// <summary>
    /// MCP tool arguments for updating a vessel.
    /// </summary>
    public class VesselUpdateArgs
    {
        /// <summary>
        /// Vessel ID (vsl_ prefix).
        /// </summary>
        public string VesselId { get; set; } = "";

        /// <summary>
        /// New display name.
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// New repository URL.
        /// </summary>
        public string? RepoUrl { get; set; }

        /// <summary>
        /// New default branch.
        /// </summary>
        public string? DefaultBranch { get; set; }

        /// <summary>
        /// New project context.
        /// </summary>
        public string? ProjectContext { get; set; }

        /// <summary>
        /// New style guide.
        /// </summary>
        public string? StyleGuide { get; set; }

        /// <summary>
        /// New local working directory where completed mission changes will be pulled after merge.
        /// </summary>
        public string? WorkingDirectory { get; set; }

        /// <summary>
        /// Whether to allow multiple concurrent missions on this vessel.
        /// </summary>
        public bool? AllowConcurrentMissions { get; set; }

        /// <summary>
        /// Whether to enable model context accumulation on this vessel.
        /// </summary>
        public bool? EnableModelContext { get; set; }

        /// <summary>
        /// Agent-accumulated context about this repository.
        /// </summary>
        public string? ModelContext { get; set; }

        /// <summary>
        /// Default pipeline ID for dispatches to this vessel (ppl_ prefix).
        /// </summary>
        public string? DefaultPipelineId { get; set; }
    }
}
