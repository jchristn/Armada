namespace Armada.Server.Mcp
{
    /// <summary>
    /// MCP tool arguments containing an event identifier.
    /// </summary>
    public class EventIdArgs
    {
        /// <summary>
        /// Event ID (evt_ prefix).
        /// </summary>
        public string EventId { get; set; } = "";
    }
}
