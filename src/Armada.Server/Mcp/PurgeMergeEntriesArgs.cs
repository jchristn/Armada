namespace Armada.Server.Mcp
{
    using System.Collections.Generic;
    using Armada.Core.Services;

    /// <summary>
    /// MCP tool arguments for batch purging merge queue entries by ID.
    /// </summary>
    public class PurgeMergeEntriesArgs
    {
        /// <summary>
        /// List of merge entry IDs to purge (mrg_ prefix).
        /// </summary>
        [CollectionBound(CollectionLimits.MaxRequestItems)]
        public List<string> EntryIds { get; set; } = new List<string>();
    }
}
