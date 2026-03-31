namespace Armada.Server.Mcp
{
    /// <summary>
    /// MCP tool arguments for updating an existing mission.
    /// </summary>
    public class MissionUpdateArgs
    {
        /// <summary>
        /// Mission ID (msn_ prefix).
        /// </summary>
        public string MissionId { get; set; } = "";

        /// <summary>
        /// New mission title.
        /// </summary>
        public string? Title { get; set; }

        /// <summary>
        /// New mission description/instructions.
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// New target vessel ID (vsl_ prefix).
        /// </summary>
        public string? VesselId { get; set; }

        /// <summary>
        /// New voyage association (vyg_ prefix).
        /// </summary>
        public string? VoyageId { get; set; }

        /// <summary>
        /// New priority (lower is higher priority).
        /// </summary>
        public int? Priority { get; set; }

        /// <summary>
        /// Git branch name for this mission.
        /// </summary>
        public string? BranchName { get; set; }

        /// <summary>
        /// Pull request URL.
        /// </summary>
        public string? PrUrl { get; set; }

        /// <summary>
        /// Parent mission ID for sub-tasks (msn_ prefix).
        /// </summary>
        public string? ParentMissionId { get; set; }

        /// <summary>
        /// Persona for this mission (e.g. Worker, Architect, Judge, TestEngineer).
        /// </summary>
        public string? Persona { get; set; }
    }
}
