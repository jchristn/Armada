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
        /// Landing mode name for created vessels (LocalMerge, MergeAndPush, PullRequest, MergeQueue, None).
        /// </summary>
        public string? LandingMode { get; set; }

        /// <summary>
        /// When true, a captain recommends fleets for the imported vessels after the import (requires CaptainId).
        /// </summary>
        public bool? Categorize { get; set; }

        /// <summary>
        /// Captain ID (cpt_ prefix) that performs fleet categorization.
        /// </summary>
        public string? CaptainId { get; set; }

        /// <summary>
        /// Categorization instructions; omit to use the import.fleet_categorization prompt template.
        /// </summary>
        public string? Prompt { get; set; }

        /// <summary>
        /// When true, apply the captain's fleet recommendations automatically when categorization completes.
        /// </summary>
        public bool? ApplyFleetsAutomatically { get; set; }
    }
}
