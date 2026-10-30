namespace Armada.Core.Models
{
    /// <summary>
    /// What a model can use as served by its runtime and provider. Null means not known. What a captain can do on its
    /// Harbor (read worktree files, run builds) is recorded on the captain, not here.
    /// </summary>
    public class ModelProfileCapabilities
    {
        /// <summary>
        /// Whether the model can call tools as served, or null when not known.
        /// </summary>
        public bool? ToolsAvailable { get; set; } = null;

        /// <summary>
        /// Whether MCP tools are available to the model as served, or null when not known.
        /// </summary>
        public bool? McpAvailable { get; set; } = null;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ModelProfileCapabilities()
        {
        }
    }
}
