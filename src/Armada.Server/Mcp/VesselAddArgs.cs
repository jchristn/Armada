namespace Armada.Server.Mcp
{
    /// <summary>
    /// MCP tool arguments for adding a vessel.
    /// </summary>
    public class VesselAddArgs
    {
        /// <summary>
        /// Display name for the vessel.
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// Git repository URL (HTTPS or SSH).
        /// </summary>
        public string RepoUrl { get; set; } = "";

        /// <summary>
        /// Fleet ID to add the vessel to.
        /// </summary>
        public string FleetId { get; set; } = "";

        /// <summary>
        /// Default branch name (defaults to main).
        /// </summary>
        public string? DefaultBranch { get; set; }

        /// <summary>
        /// Project context describing architecture, key files, and dependencies.
        /// </summary>
        public string? ProjectContext { get; set; }

        /// <summary>
        /// Style guide describing naming conventions, patterns, and library preferences.
        /// </summary>
        public string? StyleGuide { get; set; }

        /// <summary>
        /// Optional local working directory where completed mission changes will be pulled after merge.
        /// </summary>
        public string? WorkingDirectory { get; set; }
    }
}
