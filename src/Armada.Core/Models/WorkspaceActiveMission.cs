namespace Armada.Core.Models
{
    using Armada.Core.Services;

    /// <summary>
    /// One active mission relevant to a workspace.
    /// </summary>
    public class WorkspaceActiveMission
    {
        /// <summary>
        /// Mission identifier.
        /// </summary>
        public string MissionId { get; set; } = string.Empty;

        /// <summary>
        /// Mission title.
        /// </summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// Current mission status.
        /// </summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>
        /// Repository-relative scoped files inferred from the mission description.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxConfigItems, Reason = "First scoped paths parsed from the description; ScopedFileCount is the full count")]
        public List<string> ScopedFiles { get; set; } = new List<string>();

        /// <summary>
        /// Number of distinct scoped paths found in the description (ScopedFiles lists at most
        /// <see cref="CollectionLimits.MaxConfigItems"/> of them).
        /// </summary>
        public int ScopedFileCount { get; set; } = 0;
    }
}
