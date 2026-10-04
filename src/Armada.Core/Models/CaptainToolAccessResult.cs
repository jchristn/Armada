namespace Armada.Core.Models
{
    /// <summary>
    /// Describes the tool sources and named tools visible from a specific captain runtime.
    /// </summary>
    public class CaptainToolAccessResult
    {
        /// <summary>
        /// Captain identifier.
        /// </summary>
        public string CaptainId { get; set; } = String.Empty;

        /// <summary>
        /// Captain display name.
        /// </summary>
        public string CaptainName { get; set; } = String.Empty;

        /// <summary>
        /// Captain runtime name.
        /// </summary>
        public string Runtime { get; set; } = String.Empty;

        /// <summary>
        /// Whether Armada currently considers the catalog accessible through this captain.
        /// </summary>
        public bool ToolsAccessible { get; set; } = false;

        /// <summary>
        /// Whether Armada actively verified tool availability instead of inferring it.
        /// </summary>
        public bool AvailabilityVerified { get; set; } = false;

        /// <summary>
        /// Short machine-readable description of how availability was determined.
        /// </summary>
        public string AvailabilitySource { get; set; } = String.Empty;

        /// <summary>
        /// Human-readable summary of availability and caveats.
        /// </summary>
        public string Summary { get; set; } = String.Empty;

        /// <summary>
        /// Mux endpoint name, when applicable.
        /// </summary>
        public string? EndpointName { get; set; } = null;

        /// <summary>
        /// Whether the runtime reported tool calling enabled, when applicable.
        /// </summary>
        public bool? ToolsEnabled { get; set; } = null;

        /// <summary>
        /// Effective runtime-reported tool count, when applicable.
        /// </summary>
        public int? EffectiveToolCount { get; set; } = null;

        /// <summary>
        /// Number of Armada tools reported from sources named "armada".
        /// </summary>
        public int ArmadaToolCount { get; set; } = 0;

        /// <summary>
        /// Number of configured external or internal tool sources Armada inspected.
        /// </summary>
        public int ConfiguredServerCount { get; set; } = 0;

        /// <summary>
        /// True when Ask Armada thread turns run by this captain connect to Armada's MCP server with a thread-scoped token,
        /// so mutating Armada tool calls are held as approval cards. False means the captain's Armada actions in an Ask
        /// thread run without approval cards.
        /// </summary>
        public bool AskApprovalGated { get; set; } = false;

        /// <summary>
        /// Number of configured sources that Armada successfully reached.
        /// </summary>
        public int ReachableServerCount { get; set; } = 0;

        /// <summary>
        /// Source summaries discovered for this captain runtime.
        /// </summary>
        public List<CaptainToolServerSummary> Servers { get; set; } = new List<CaptainToolServerSummary>();

        /// <summary>
        /// Tools currently described for this captain runtime.
        /// </summary>
        public List<CaptainToolSummary> Tools { get; set; } = new List<CaptainToolSummary>();
    }
}
