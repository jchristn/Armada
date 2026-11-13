namespace Armada.Core.Models
{
    /// <summary>
    /// Result of resolving an observation to a model profile: the profile and its current revision, and whether either
    /// was created by this call.
    /// </summary>
    public class ModelProfileResolution
    {
        /// <summary>
        /// The profile.
        /// </summary>
        public ModelProfile Profile { get; set; } = new ModelProfile();

        /// <summary>
        /// The current revision (pooled into or new).
        /// </summary>
        public ModelProfileRevision Revision { get; set; } = new ModelProfileRevision();

        /// <summary>
        /// True when the profile was created by this call.
        /// </summary>
        public bool ProfileCreated { get; set; } = false;

        /// <summary>
        /// True when the revision was created by this call.
        /// </summary>
        public bool RevisionCreated { get; set; } = false;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ModelProfileResolution()
        {
        }
    }
}
