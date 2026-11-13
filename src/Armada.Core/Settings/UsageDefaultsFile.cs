namespace Armada.Core.Settings
{
    /// <summary>Minimal file envelope for detecting pre-revision-2 usage settings.</summary>
    internal class UsageDefaultsFile
    {
        /// <summary>Persisted usage default revision.</summary>
        public UsageDefaultsRevision? UsageCapture { get; set; }
    }
}
