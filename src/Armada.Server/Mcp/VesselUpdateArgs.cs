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
    }
}
