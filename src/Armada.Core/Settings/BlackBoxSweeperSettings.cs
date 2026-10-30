namespace Armada.Core.Settings
{
    using System;

    /// <summary>
    /// Black box sweeper settings (blackBox.sweeper): the background pass that captures terminal failures without a
    /// current entry (CaptureMode Sweeper) and requeues EnrichmentFailed entries with exponential backoff.
    /// </summary>
    public class BlackBoxSweeperSettings
    {
        /// <summary>
        /// Seconds between sweeps. Default 60, clamped to 10..86400.
        /// </summary>
        public int IntervalSeconds
        {
            get => _IntervalSeconds;
            set => _IntervalSeconds = Math.Clamp(value, 10, 86400);
        }

        /// <summary>
        /// Missions or voyages read per page. Default 100, clamped to 1..1000.
        /// </summary>
        public int PageSize
        {
            get => _PageSize;
            set => _PageSize = Math.Clamp(value, 1, 1000);
        }

        /// <summary>
        /// Enrichment attempts before an entry stays EnrichmentFailed. Default 5, clamped to 1..20.
        /// </summary>
        public int MaxEnrichmentAttempts
        {
            get => _MaxEnrichmentAttempts;
            set => _MaxEnrichmentAttempts = Math.Clamp(value, 1, 20);
        }

        private int _IntervalSeconds = 60;
        private int _PageSize = 100;
        private int _MaxEnrichmentAttempts = 5;

        /// <summary>
        /// Instantiate with defaults.
        /// </summary>
        public BlackBoxSweeperSettings()
        {
        }
    }
}
