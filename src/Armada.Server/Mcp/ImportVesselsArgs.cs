namespace Armada.Server.Mcp
{
    using System.Collections.Generic;

    /// <summary>
    /// MCP tool arguments for importing candidates from a discovered batch.
    /// </summary>
    public class ImportVesselsArgs
    {
        /// <summary>
        /// Batch ID (vib_ prefix) returned by discover_vessels.
        /// </summary>
        public string BatchId { get; set; } = "";

        /// <summary>
        /// Candidate paths to import. Ignored when AllNew is true.
        /// </summary>
        public List<string>? Paths { get; set; }

        /// <summary>
        /// When true, import every candidate with status New.
        /// </summary>
        public bool? AllNew { get; set; }

        /// <summary>
        /// Fleet ID (flt_ prefix) to assign created vessels to.
        /// </summary>
        public string? FleetId { get; set; }

        /// <summary>
        /// Default pipeline ID (ppl_ prefix) for created vessels.
        /// </summary>
        public string? DefaultPipelineId { get; set; }

        /// <summary>
        /// Landing mode name for created vessels (LocalMerge, PullRequest, MergeQueue, None).
        /// </summary>
        public string? LandingMode { get; set; }
    }
}
