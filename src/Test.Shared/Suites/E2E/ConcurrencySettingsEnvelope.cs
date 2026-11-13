namespace Test.Shared.Suites.E2E
{
    using Armada.Core.Settings;

    /// <summary>The Concurrency members of a GET or PUT /api/v1/settings response.</summary>
    public sealed class ConcurrencySettingsEnvelope
    {
        /// <summary>The Concurrency group.</summary>
        public ConcurrencySettings Concurrency { get; set; } = new ConcurrencySettings();

        /// <summary>The group's optimistic-update revision.</summary>
        public long ConcurrencyRevision { get; set; }
    }
}
