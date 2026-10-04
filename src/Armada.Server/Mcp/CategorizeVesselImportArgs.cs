namespace Armada.Server.Mcp
{
    /// <summary>
    /// MCP tool arguments for running or retrying fleet categorization of an import batch.
    /// </summary>
    public class CategorizeVesselImportArgs
    {
        /// <summary>
        /// Batch ID (vib_ prefix).
        /// </summary>
        public string BatchId { get; set; } = "";

        /// <summary>
        /// Captain ID (cpt_ prefix); omit to reuse the batch's previous captain.
        /// </summary>
        public string? CaptainId { get; set; }

        /// <summary>
        /// Instructions; omit to reuse the previous run's instructions.
        /// </summary>
        public string? Prompt { get; set; }

        /// <summary>
        /// When set, whether to apply the recommendations automatically; omit to reuse the previous choice.
        /// </summary>
        public bool? ApplyFleetsAutomatically { get; set; }
    }
}
