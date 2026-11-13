namespace Armada.Server.Mcp
{
    /// <summary>
    /// MCP tool arguments for voyage_status.
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
        /// Removed. Missions are never embedded; true is rejected with InvalidArgument naming the replacement (enumerate
        /// with entityType missions and voyageId).
        /// </summary>
        public bool? IncludeMissions { get; set; }
    }
}
