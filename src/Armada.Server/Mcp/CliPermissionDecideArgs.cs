namespace Armada.Server.Mcp
{
    /// <summary>
    /// MCP arguments for decide_cli_permission_request.
    /// </summary>
    public class CliPermissionDecideArgs
    {
        /// <summary>
        /// Request ID (cpr_ prefix).
        /// </summary>
        public string RequestId { get; set; } = "";

        /// <summary>
        /// AllowOnce, AllowAndRemember, or Deny.
        /// </summary>
        public string Decision { get; set; } = "";

        /// <summary>
        /// Optional message (returned to the captain with a denial).
        /// </summary>
        public string? Message { get; set; } = null;

        /// <summary>
        /// Rule pattern for AllowAndRemember; defaults to the suggested rule.
        /// </summary>
        public string? RulePattern { get; set; } = null;

        /// <summary>
        /// Rule scope for AllowAndRemember: Global, Vessel, or Captain (default Captain).
        /// </summary>
        public string? RuleScope { get; set; } = null;
    }
}
