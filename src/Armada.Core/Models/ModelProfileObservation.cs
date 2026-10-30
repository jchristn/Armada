namespace Armada.Core.Models
{
    /// <summary>
    /// What Armada observed about the model a captain runs (from a stage outcome's provenance or a Quick probe run), used
    /// to find or create its model profile and current revision.
    /// </summary>
    public class ModelProfileObservation
    {
        /// <summary>
        /// Tenant of the captain, or null.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// Captain to link to the profile, or null.
        /// </summary>
        public string? CaptainId { get; set; } = null;

        /// <summary>
        /// Runtime (required).
        /// </summary>
        public string Runtime { get; set; } = "";

        /// <summary>
        /// Inference provider, or null.
        /// </summary>
        public string? Provider { get; set; } = null;

        /// <summary>
        /// Model name, or null.
        /// </summary>
        public string? Model { get; set; } = null;

        /// <summary>
        /// Served context in tokens, or null.
        /// </summary>
        public int? ServedContextTokens { get; set; } = null;

        /// <summary>
        /// Runtime version, or null when not known.
        /// </summary>
        public string? RuntimeVersion { get; set; } = null;

        /// <summary>
        /// Model digest, or null when not known.
        /// </summary>
        public string? ModelDigest { get; set; } = null;

        /// <summary>
        /// Temperature, or null.
        /// </summary>
        public double? Temperature { get; set; } = null;

        /// <summary>
        /// Top-p, or null.
        /// </summary>
        public double? TopP { get; set; } = null;

        /// <summary>
        /// Whether tools were enabled, or null.
        /// </summary>
        public bool? ToolsEnabled { get; set; } = null;

        /// <summary>
        /// Whether MCP tools were enabled, or null.
        /// </summary>
        public bool? McpToolsEnabled { get; set; } = null;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ModelProfileObservation()
        {
        }
    }
}
