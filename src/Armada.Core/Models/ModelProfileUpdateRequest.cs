namespace Armada.Core.Models
{
    /// <summary>
    /// Update of the mutable parts of a model profile. A section the body omits (null) keeps the stored value; a section
    /// the body carries replaces it whole, so a null value inside it clears that value. An empty Notes string clears the
    /// notes. The identity and the revisions are never changed by an update.
    /// </summary>
    public class ModelProfileUpdateRequest
    {
        /// <summary>
        /// Replacement capabilities, or null to keep them.
        /// </summary>
        public ModelProfileCapabilities? Capabilities { get; set; } = null;

        /// <summary>
        /// Replacement rendering overrides, or null to keep them.
        /// </summary>
        public ModelProfileRendering? Rendering { get; set; } = null;

        /// <summary>
        /// Replacement notes (empty clears), or null to keep them.
        /// </summary>
        public string? Notes { get; set; } = null;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ModelProfileUpdateRequest()
        {
        }
    }
}
