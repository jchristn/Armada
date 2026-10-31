namespace Armada.Core.Models
{
    using System.Collections.Generic;
    using Armada.Core.Services;

    /// <summary>
    /// Replaces the set of revisions an administrator marked compatible with one revision. Compatibility is symmetric:
    /// each listed revision is also marked compatible with the revision. An empty list removes every mark.
    /// </summary>
    public class ModelProfileCompatibilityRequest
    {
        /// <summary>
        /// Revision identifiers (mdv_ prefix) of the same tenant.
        /// </summary>
        [CollectionBound(CollectionLimits.MaxRequestItems, Reason = "At most ModelProfileRevision.MaxCompatibleWith distinct revisions after duplicates and the revision itself are dropped")]
        public List<string>? CompatibleWith { get; set; } = null;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ModelProfileCompatibilityRequest()
        {
        }
    }
}
