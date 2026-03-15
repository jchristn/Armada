namespace Armada.Server.Mcp
{
    /// <summary>
    /// MCP tool arguments for armada_voyage_status.
    /// </summary>
    public class VoyageStatusArgs
    {
        /// <summary>
        /// Voyage ID (vyg_ prefix).
        /// </summary>
        public string VoyageId { get; set; } = "";

        /// <summary>
        /// Return summary only: voyage metadata plus mission counts by status (default true).
        /// When true, no mission objects are embedded.
        /// </summary>
        public bool? Summary { get; set; }

        /// <summary>
        /// Include full mission objects in the response (default false). Only applies when summary is false.
        /// </summary>
        public bool? IncludeMissions { get; set; }

        /// <summary>
        /// Include Description field on embedded missions (default false).
        /// </summary>
        public bool? IncludeDescription { get; set; }

        /// <summary>
        /// Include saved diff for each mission (default false).
        /// </summary>
        public bool? IncludeDiffs { get; set; }

        /// <summary>
        /// Include log excerpt for each mission (default false).
        /// Currently reserved for future use -- logs are stored in external files
        /// and are not available on the mission object.
        /// </summary>
        public bool? IncludeLogs { get; set; }
    }
}
