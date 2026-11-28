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
        /// Replacement recovery policy overrides, or null to keep them.
        /// </summary>
        public ModelProfileRecovery? Recovery { get; set; } = null;

        /// <summary>
        /// Replacement timeout overrides (RS-8.8), or null to keep them.
        /// </summary>
        public ModelProfileTimeouts? Timeouts { get; set; } = null;

        /// <summary>
        /// Replacement tier, or null to keep it. Only <see cref="ModelProfileTiering.Tier"/> is read (null clears the
        /// administrator's tier); the suggestion fields are written by the model alert pass and ignored here (RS-8.5).
        /// </summary>
        public ModelProfileTiering? Tiering { get; set; } = null;

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
