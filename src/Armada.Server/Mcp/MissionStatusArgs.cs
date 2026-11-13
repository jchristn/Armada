namespace Armada.Server.Mcp
{
    /// <summary>MCP tool arguments for mission_status.</summary>
    public class MissionStatusArgs
    {
        /// <summary>Mission ID (msn_ prefix).</summary>
        public string MissionId { get; set; } = "";

        /// <summary>Include the mission's recorded wait intervals as Waits (default false).</summary>
        public bool? IncludeWaits { get; set; }
    }
}
