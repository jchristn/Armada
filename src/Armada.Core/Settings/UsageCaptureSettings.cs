namespace Armada.Core.Settings
{
    /// <summary>
    /// Usage capture settings (ArmadaSettings.UsageCapture): the server-wide switch and bounds for account
    /// usage capture. The switch disables scheduled and manual active probes and passive limit persistence
    /// when off; existing token recording continues either way. Changing settings cancels queued capture
    /// work and invalidates the scheduler's generation. Interval bounds are clamped here so the model, the
    /// settings file and every API agree on the effective value. Retention days bound the raw snapshot and
    /// hourly rollup windows used by the retention pass.
    /// </summary>
    public class UsageCaptureSettings
    {
        #region Public-Members

        /// <summary>
        /// Whether account usage capture runs at all. Default false: capture is disabled by default and
        /// only a captain that is enabled and not in Disabled mode participates. Turning it off stops
        /// scheduled and manual active probes and passive limit persistence, but never token recording.
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// Global floor for the effective capture interval, in seconds. Default 60, clamped to 60..86400.
        /// Groups run at the shortest requested interval among enabled members, bounded by this floor.
        /// </summary>
        public int MinimumIntervalSeconds
        {
            get => _MinimumIntervalSeconds;
            set => _MinimumIntervalSeconds = value < 60 ? 60 : (value > 86400 ? 86400 : value);
        }

        /// <summary>
        /// Whether the opt-in PTY screen-scraping source may be used at all. Default false; PTY is never an
        /// automatic fallback, so with this off a PTY probe is refused regardless of a captain's request.
        /// </summary>
        public bool AllowPtyFallback { get; set; } = false;

        /// <summary>
        /// Host execution deadline for one probe, in seconds. Default 20, clamped to 1..300. The process
        /// tree is terminated on expiry; the round-trip deadline the Admiral allows is longer.
        /// </summary>
        public int ProbeTimeoutSeconds
        {
            get => _ProbeTimeoutSeconds;
            set => _ProbeTimeoutSeconds = value < 1 ? 1 : (value > 300 ? 300 : value);
        }

        /// <summary>
        /// Server-wide maximum number of probes running at once, separate from mission launch slots.
        /// Default 4, clamped to 1..64.
        /// </summary>
        public int MaximumConcurrentProbes
        {
            get => _MaximumConcurrentProbes;
            set => _MaximumConcurrentProbes = value < 1 ? 1 : (value > 64 ? 64 : value);
        }

        /// <summary>
        /// Maximum number of probes running at once on one credential host. Default 1, clamped to 1..64.
        /// </summary>
        public int MaximumConcurrentProbesPerHost
        {
            get => _MaximumConcurrentProbesPerHost;
            set => _MaximumConcurrentProbesPerHost = value < 1 ? 1 : (value > 64 ? 64 : value);
        }

        /// <summary>
        /// Maximum failure backoff, in seconds. Default 3600, clamped to 60..86400. Backoff grows 2x per
        /// consecutive failure up to this bound and honors a longer provider Retry-After.
        /// </summary>
        public int MaximumBackoffSeconds
        {
            get => _MaximumBackoffSeconds;
            set => _MaximumBackoffSeconds = value < 60 ? 60 : (value > 86400 ? 86400 : value);
        }

        /// <summary>
        /// Cap for a stored sanitized raw excerpt, in UTF-8 bytes. Default 2048, clamped to 256..65536.
        /// </summary>
        public int MaximumExcerptBytes
        {
            get => _MaximumExcerptBytes;
            set => _MaximumExcerptBytes = value < 256 ? 256 : (value > 65536 ? 65536 : value);
        }

        /// <summary>
        /// Days a raw usage snapshot (and its member links and excerpt) is retained before the retention
        /// pass deletes it. Default 7, clamped to 1..365. Token history is bounded independently by its own
        /// retention policy.
        /// </summary>
        public int RawSnapshotRetentionDays
        {
            get => _RawSnapshotRetentionDays;
            set => _RawSnapshotRetentionDays = value < 1 ? 1 : (value > 365 ? 365 : value);
        }

        /// <summary>
        /// Days an hourly usage rollup is retained. Default 90, clamped to 7..1825.
        /// </summary>
        public int RollupRetentionDays
        {
            get => _RollupRetentionDays;
            set => _RollupRetentionDays = value < 7 ? 7 : (value > 1825 ? 1825 : value);
        }

        #endregion

        #region Private-Members

        private int _MinimumIntervalSeconds = 60;
        private int _ProbeTimeoutSeconds = 20;
        private int _MaximumConcurrentProbes = 4;
        private int _MaximumConcurrentProbesPerHost = 1;
        private int _MaximumBackoffSeconds = 3600;
        private int _MaximumExcerptBytes = 2048;
        private int _RawSnapshotRetentionDays = 7;
        private int _RollupRetentionDays = 90;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate with defaults.
        /// </summary>
        public UsageCaptureSettings()
        {
        }

        #endregion
    }
}