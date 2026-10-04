namespace Test.Shared.Infrastructure
{
    using System.Collections.Generic;

    /// <summary>
    /// The slice of publisher.json the registration tests compare against the registration flags.
    /// </summary>
    public sealed class PublisherManifestView
    {
        /// <summary>
        /// Artifacts declared in the manifest.
        /// </summary>
        public List<PublisherArtifactView> Artifacts { get; set; } = new List<PublisherArtifactView>();
    }
}
