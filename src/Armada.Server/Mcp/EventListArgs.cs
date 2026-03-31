namespace Armada.Server.Mcp
{
    /// <summary>
    /// MCP tool arguments for listing events with optional filters.
    /// </summary>
    public class EventListArgs
    {
        /// <summary>
        /// Filter events by mission ID.
        /// </summary>
        public string? MissionId { get; set; }

        /// <summary>
        /// Filter events by captain ID.
        /// </summary>
        public string? CaptainId { get; set; }

        /// <summary>
        /// Filter events by voyage ID.
        /// </summary>
        public string? VoyageId { get; set; }

        /// <summary>
        /// Maximum number of events to return (default 50).
        /// </summary>
        public int? Limit { get; set; }
    }
}
