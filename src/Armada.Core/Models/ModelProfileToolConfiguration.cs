namespace Armada.Core.Models
{
    /// <summary>
    /// Tool configuration of a model profile revision as first seen. Null means not known.
    /// </summary>
    public class ModelProfileToolConfiguration
    {
        /// <summary>
        /// Whether tools were enabled, or null.
        /// </summary>
        public bool? Tools { get; set; } = null;

        /// <summary>
        /// Whether MCP tools were enabled, or null.
        /// </summary>
        public bool? Mcp { get; set; } = null;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ModelProfileToolConfiguration()
        {
        }
    }
}
