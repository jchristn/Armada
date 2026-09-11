namespace Armada.Server.Mcp
{
    /// <summary>
    /// MCP tool arguments for searching memories. Used by the Recorder to find existing memories to
    /// consolidate against before writing.
    /// </summary>
    public class MemorySearchArgs
    {
        /// <summary>
        /// Optional case-insensitive substring matched across content, summary-adjacent content, topic, and tags.
        /// </summary>
        public string? Search { get; set; }

        /// <summary>
        /// Optional type filter: Episodic, Semantic, or Procedural.
        /// </summary>
        public string? Type { get; set; }

        /// <summary>
        /// Optional exact topic filter.
        /// </summary>
        public string? Topic { get; set; }

        /// <summary>
        /// Optional vessel filter (matches either the association or the provenance vessel).
        /// </summary>
        public string? VesselId { get; set; }

        /// <summary>
        /// Page number (1-based). Defaults to 1.
        /// </summary>
        public int? PageNumber { get; set; }

        /// <summary>
        /// Page size. Defaults to the enumeration default.
        /// </summary>
        public int? PageSize { get; set; }
    }
}
