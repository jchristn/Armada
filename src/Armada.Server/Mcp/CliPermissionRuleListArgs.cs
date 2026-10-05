namespace Armada.Server.Mcp
{
    /// <summary>
    /// MCP arguments for list_cli_permission_rules.
    /// </summary>
    public class CliPermissionRuleListArgs
    {
        /// <summary>
        /// Scope filter (Global, Vessel, Captain), or null.
        /// </summary>
        public string? Scope { get; set; } = null;

        /// <summary>
        /// Vessel filter, or null.
        /// </summary>
        public string? VesselId { get; set; } = null;

        /// <summary>
        /// Captain filter, or null.
        /// </summary>
        public string? CaptainId { get; set; } = null;
    }
}
