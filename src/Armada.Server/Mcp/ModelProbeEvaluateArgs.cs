namespace Armada.Server.Mcp
{
    using System.Collections.Generic;

    /// <summary>
    /// MCP arguments for evaluate_captain: exactly one of captainId or modelEndpointId, an optional depth, and for depth
    /// Full the optional evaluator, packs, personas and run count.
    /// </summary>
    public class ModelProbeEvaluateArgs
    {
        /// <summary>
        /// Captain to evaluate (cpt_ prefix), or null.
        /// </summary>
        public string? CaptainId { get; set; } = null;

        /// <summary>
        /// Model endpoint to evaluate (mep_ prefix), or null.
        /// </summary>
        public string? ModelEndpointId { get; set; } = null;

        /// <summary>
        /// Depth: Quick (default) or Full.
        /// </summary>
        public string? Depth { get; set; } = null;

        /// <summary>
        /// Full only: evaluator captain (cpt_ prefix), or null for the default.
        /// </summary>
        public string? EvaluatorCaptainId { get; set; } = null;

        /// <summary>
        /// Full only: battery packs, or null for the packs matching the tenant's repository languages.
        /// </summary>
        public List<string>? Packs { get; set; } = null;

        /// <summary>
        /// Full only: personas to score, or null for all the captain is allowed.
        /// </summary>
        public List<string>? Personas { get; set; } = null;

        /// <summary>
        /// Full only: runs of each battery task (1 to 10), or null for 3.
        /// </summary>
        public int? RunCount { get; set; } = null;
    }
}
