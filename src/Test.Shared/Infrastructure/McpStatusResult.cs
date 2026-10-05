namespace Test.Shared.Infrastructure
{
    /// <summary>
    /// Result of an Armada MCP tool that reports an action outcome as a status word plus the affected id (for example
    /// stop_captain, delete_fleet, cancel_merge_entry). Deserialized from the tool result text so tests compare the status exactly.
    /// </summary>
    public class McpStatusResult
    {
        #region Public-Members

        /// <summary>
        /// Status word the tool reports (for example "deleted", "stopped", "all_stopped"), or null.
        /// </summary>
        public string? Status { get; set; } = null;

        /// <summary>
        /// Success flag some tools report instead of a status, or null.
        /// </summary>
        public bool? Success { get; set; } = null;

        /// <summary>
        /// Captain id, or null.
        /// </summary>
        public string? CaptainId { get; set; } = null;

        /// <summary>
        /// Fleet id, or null.
        /// </summary>
        public string? FleetId { get; set; } = null;

        /// <summary>
        /// Vessel id, or null.
        /// </summary>
        public string? VesselId { get; set; } = null;

        /// <summary>
        /// Mission id, or null.
        /// </summary>
        public string? MissionId { get; set; } = null;

        /// <summary>
        /// Voyage id, or null.
        /// </summary>
        public string? VoyageId { get; set; } = null;

        /// <summary>
        /// Merge queue entry id, or null.
        /// </summary>
        public string? EntryId { get; set; } = null;

        /// <summary>
        /// Objective (backlog item) id, or null.
        /// </summary>
        public string? ObjectiveId { get; set; } = null;

        /// <summary>
        /// Playbook id, or null.
        /// </summary>
        public string? PlaybookId { get; set; } = null;

        /// <summary>
        /// Memory id, or null.
        /// </summary>
        public string? MemoryId { get; set; } = null;

        #endregion
    }
}
