namespace Armada.Core.Models
{
    /// <summary>
    /// Sampling settings of a model profile revision. Null means the runtime default (not known to Armada).
    /// </summary>
    public class ModelProfileSampling
    {
        /// <summary>
        /// Temperature, or null.
        /// </summary>
        public double? Temperature { get; set; } = null;

        /// <summary>
        /// Top-p (nucleus sampling), or null.
        /// </summary>
        public double? TopP { get; set; } = null;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ModelProfileSampling()
        {
        }
    }
}
