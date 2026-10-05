namespace Armada.Server.Mcp
{
    /// <summary>
    /// MCP arguments for set_captain_cli_permission_policy.
    /// </summary>
    public class CaptainCliPermissionPolicyArgs
    {
        /// <summary>
        /// Captain ID (cpt_ prefix).
        /// </summary>
        public string CaptainId { get; set; } = "";

        /// <summary>
        /// Refuse, ApproveInArmada, or Bypass; omit or null to clear (inherit).
        /// </summary>
        public string? Policy { get; set; } = null;
    }
}
