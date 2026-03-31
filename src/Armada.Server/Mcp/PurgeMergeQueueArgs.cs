namespace Armada.Server.Mcp
{
    /// <summary>
    /// MCP tool arguments for purging terminal merge queue entries.
    /// </summary>
    public class PurgeMergeQueueArgs
    {
        /// <summary>
        /// Optional vessel ID filter (vsl_ prefix).
        /// </summary>
        public string? VesselId { get; set; }

        /// <summary>
        /// Optional status filter (Landed, Failed, or Cancelled).
        /// </summary>
        public string? Status { get; set; }
    }
}
