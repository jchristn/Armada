namespace Armada.Core.Models
{
    /// <summary>
    /// High-level workspace status for one vessel.
    /// </summary>
    public class WorkspaceStatusResult
    {
        /// <summary>
        /// Vessel identifier.
        /// </summary>
        public string VesselId { get; set; } = string.Empty;

        /// <summary>
        /// True if the vessel has a configured working directory that exists.
        /// </summary>
        public bool HasWorkingDirectory { get; set; }

        /// <summary>
        /// Absolute workspace root path when available.
        /// </summary>
        public string? RootPath { get; set; }

        /// <summary>
        /// Current branch name, if available.
        /// </summary>
        public string? BranchName { get; set; }

        /// <summary>
        /// True if the working tree has any local changes.
        /// </summary>
        public bool IsDirty { get; set; }

        /// <summary>
        /// Commits ahead of the tracking branch.
        /// </summary>
        public int? CommitsAhead { get; set; }

        /// <summary>
        /// Commits behind the tracking branch.
        /// </summary>
        public int? CommitsBehind { get; set; }

        /// <summary>
        /// Number of active missions on this vessel.
        /// </summary>
        public int ActiveMissionCount { get; set; }

        /// <summary>
        /// Active missions and their inferred file scopes.
        /// </summary>
        public List<WorkspaceActiveMission> ActiveMissions { get; set; } = new List<WorkspaceActiveMission>();

        /// <summary>
        /// Optional status error when repository inspection fails, or why the vessel has no checkout Armada can use.
        /// </summary>
        public string? Error { get; set; }

        /// <summary>
        /// The Harbor that has the checkout Workspace works in, or null for the Admiral host.
        /// </summary>
        public string? HarborId { get; set; }

        /// <summary>
        /// Where the checkout is: "Admiral", or "Harbor Name (hbr_...)".
        /// </summary>
        public string Host { get; set; } = "Admiral";
    }
}
