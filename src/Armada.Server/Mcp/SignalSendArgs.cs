namespace Armada.Server.Mcp
{
    /// <summary>
    /// MCP tool arguments for sending a signal to a captain.
    /// </summary>
    public class SignalSendArgs
    {
        /// <summary>
        /// Target captain ID.
        /// </summary>
        public string CaptainId { get; set; } = "";

        /// <summary>
        /// Signal message.
        /// </summary>
        public string Message { get; set; } = "";
    }
}
