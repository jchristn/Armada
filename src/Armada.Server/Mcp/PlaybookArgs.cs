namespace Armada.Server.Mcp
{
    /// <summary>
    /// MCP tool arguments for playbook CRUD operations.
    /// </summary>
    public class PlaybookArgs
    {
        /// <summary>
        /// Playbook identifier.
        /// </summary>
        public string? Id { get; set; }

        /// <summary>
        /// Playbook file name.
        /// </summary>
        public string? FileName { get; set; }

        /// <summary>
        /// Optional human-readable description.
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// Markdown body.
        /// </summary>
        public string? Content { get; set; }

        /// <summary>
        /// Whether the playbook is active.
        /// </summary>
        public bool? Active { get; set; }
    }
}
