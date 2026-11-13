namespace Armada.Server.Mcp
{
    using System.Collections.Generic;

    /// <summary>
    /// Arguments of delete_blackbox_entries.
    /// </summary>
    public class BlackBoxDeleteArgs
    {
        /// <summary>
        /// Entries to delete (bbx_ prefix, at most 1000).
        /// </summary>
        public List<string>? EntryIds { get; set; } = null;
    }
}
