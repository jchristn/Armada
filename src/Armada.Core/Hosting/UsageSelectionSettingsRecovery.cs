namespace Armada.Core.Hosting
{
    /// <summary>Protected write-ahead rollback data for a potentially bind-mounted settings file.</summary>
    internal sealed class UsageSelectionSettingsRecovery
    {
        /// <summary>Whether the original settings file existed before the attempted update.</summary>
        public bool OriginalExists { get; set; }
        /// <summary>Protected original unmanaged settings document; never include in diagnostics.</summary>
        public string OriginalContent { get; set; } = string.Empty;
    }
}
