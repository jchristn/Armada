namespace Armada.Server.Mcp
{
    using System.Collections.Generic;

    /// <summary>
    /// MCP tool arguments for batch purging merge queue entries by ID.
    /// </summary>
    public class PurgeMergeEntriesArgs
    {
        /// <summary>
        /// List of merge entry IDs to purge (mrg_ prefix).
        /// </summary>
        public List<string> EntryIds { get; set; } = new List<string>();
    }
}
