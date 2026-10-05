namespace Armada.Server.Mcp
{
    /// <summary>
    /// MCP arguments naming a CLI permission request.
    /// </summary>
    public class CliPermissionRequestIdArgs
    {
        /// <summary>
        /// Request ID (cpr_ prefix).
        /// </summary>
        public string RequestId { get; set; } = "";
    }
}
