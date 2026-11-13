namespace Armada.Core.Models
{
    using Armada.Core.Settings;

    /// <summary>Detached effective policy, its revision and operator capability metadata.</summary>
    public sealed class UsageSelectionConfiguration
    {
        /// <summary>Effective value-only settings snapshot.</summary>
        public UsageSelectionSettings Settings { get; set; } = new UsageSelectionSettings();
        /// <summary>Persisted monotonic settings revision, used for optimistic updates.</summary>
        public long Revision { get; set; }
        /// <summary>Live/override/enforcement availability metadata.</summary>
        public UsageSelectionCapabilities Capabilities { get; set; } = new UsageSelectionCapabilities();
    }
}
