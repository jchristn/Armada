namespace Armada.Server.Mcp
{
    /// <summary>
    /// MCP tool arguments for adding a branch to the merge queue.
    /// </summary>
    public class MergeEnqueueArgs
    {
        /// <summary>
        /// Associated mission ID (msn_ prefix).
        /// </summary>
        public string? MissionId { get; set; }

        /// <summary>
        /// Target vessel ID (vsl_ prefix).
        /// </summary>
        public string VesselId { get; set; } = "";

        /// <summary>
        /// Branch name to merge.
        /// </summary>
        public string BranchName { get; set; } = "";

        /// <summary>
        /// Target branch (defaults to main).
        /// </summary>
        public string? TargetBranch { get; set; }

        /// <summary>
        /// Queue priority (lower = higher, default 0).
        /// </summary>
        public int? Priority { get; set; }

        /// <summary>
        /// Custom test command to run.
        /// </summary>
        public string? TestCommand { get; set; }
    }
}
