namespace Armada.Server.Mcp
{
    using System.Collections.Generic;

    /// <summary>
    /// MCP tool arguments for batch deleting multiple entities by ID.
    /// </summary>
    public class DeleteMultipleArgs
    {
        /// <summary>
        /// List of entity IDs to delete.
        /// </summary>
        public List<string> Ids { get; set; } = new List<string>();
    }
}
