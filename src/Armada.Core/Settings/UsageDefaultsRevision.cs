namespace Armada.Core.Settings
{
    /// <summary>Persisted revision, with zero indicating a file written before the reset.</summary>
    internal class UsageDefaultsRevision
    {
        /// <summary>Revision recorded in the original file.</summary>
        public int DefaultsRevision { get; set; }
    }
}
