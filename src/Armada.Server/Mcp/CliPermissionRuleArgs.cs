namespace Armada.Server.Mcp
{
    /// <summary>
    /// MCP arguments for create_cli_permission_rule and update_cli_permission_rule.
    /// </summary>
    public class CliPermissionRuleArgs
    {
        /// <summary>
        /// Rule ID (cpl_ prefix); required for update.
        /// </summary>
        public string? RuleId { get; set; } = null;

        /// <summary>
        /// Rule in Claude Code permission rule syntax, for example Bash(git status:*).
        /// </summary>
        public string Pattern { get; set; } = "";

        /// <summary>
        /// Allow or Deny.
        /// </summary>
        public string Action { get; set; } = "";

        /// <summary>
        /// Global, Vessel, or Captain (create only; default Global).
        /// </summary>
        public string? Scope { get; set; } = null;

        /// <summary>
        /// Vessel for a Vessel rule (create only).
        /// </summary>
        public string? VesselId { get; set; } = null;

        /// <summary>
        /// Captain for a Captain rule (create only).
        /// </summary>
        public string? CaptainId { get; set; } = null;

        /// <summary>
        /// Tenant for a Global rule created by a global admin; omit for every tenant (create only).
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// Optional note.
        /// </summary>
        public string? Description { get; set; } = null;
    }
}
