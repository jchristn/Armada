namespace Armada.Core.Settings
{
    using System;

    /// <summary>
    /// Mission and voyage black box settings (ArmadaSettings.BlackBox, JSON "blackBox"): capture, storage, retention and the
    /// sweeper. Never null; setting a nested object to null restores its defaults. Written back with every property.
    /// See docs/BLACKBOX.md.
    /// </summary>
    public class BlackBoxSettings
    {
        #region Public-Members

        /// <summary>
        /// Whether failures are captured. Default true. When false, no entries are written, the sweeper and garbage
        /// collector are idle, and existing entries stay readable.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Time budget for one capture beyond the index insert, in milliseconds. Default 2000, clamped to 100..30000. On
        /// timeout the capture writes what it has and notes the timeout in the record's Truncation section.
        /// </summary>
        public int CaptureTimeoutMs
        {
            get => _CaptureTimeoutMs;
            set => _CaptureTimeoutMs = Math.Clamp(value, 100, 30000);
        }

        /// <summary>
        /// Where object bytes are stored. Never null.
        /// </summary>
        public BlackBoxStorageSettings Storage
        {
            get => _Storage;
            set => _Storage = value ?? new BlackBoxStorageSettings();
        }

        /// <summary>
        /// Retention, expiration and garbage collection policy. Never null.
        /// </summary>
        public BlackBoxRetentionSettings Retention
        {
            get => _Retention;
            set => _Retention = value ?? new BlackBoxRetentionSettings();
        }

        /// <summary>
        /// Sweeper that captures failures without a current entry and requeues failed enrichment. Never null.
        /// </summary>
        public BlackBoxSweeperSettings Sweeper
        {
            get => _Sweeper;
            set => _Sweeper = value ?? new BlackBoxSweeperSettings();
        }

        /// <summary>
        /// Model endpoint (mep_ prefix) asked to write a labeled Analysis.Summary during enrichment, or null for none
        /// (default). Rule findings stay authoritative.
        /// </summary>
        public string? SummaryEndpointId
        {
            get => _SummaryEndpointId;
            set => _SummaryEndpointId = String.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        /// <summary>
        /// Black box entries that must record conflicts in a directory before an active mission's scoped files there
        /// reserve it (soft directory reservations). Default 3, clamped to 0..1000; 0 turns reservations off.
        /// </summary>
        public int HotspotReservationThreshold
        {
            get => _HotspotReservationThreshold;
            set => _HotspotReservationThreshold = Math.Clamp(value, 0, 1000);
        }

        /// <summary>
        /// Days the success flight recorder keeps its minimal records of successful landings. Default 7, clamped to
        /// 0..90; 0 turns the recorder off (nothing is recorded and existing records are removed by the next cleanup).
        /// </summary>
        public int SuccessRetentionDays
        {
            get => _SuccessRetentionDays;
            set => _SuccessRetentionDays = Math.Clamp(value, 0, 90);
        }

        /// <summary>
        /// Hours on either side of a failure in which a successful landing on the same vessel counts as nearby. Default
        /// 24, clamped to 1..168.
        /// </summary>
        public int NearbySuccessWindowHours
        {
            get => _NearbySuccessWindowHours;
            set => _NearbySuccessWindowHours = Math.Clamp(value, 1, 168);
        }

        /// <summary>
        /// Interval of the insight worker (success recording, success cleanup, recovery outcome watching), in seconds.
        /// Default 60, clamped to 5..3600.
        /// </summary>
        public int InsightIntervalSeconds
        {
            get => _InsightIntervalSeconds;
            set => _InsightIntervalSeconds = Math.Clamp(value, 5, 3600);
        }

        #endregion

        #region Private-Members

        private int _CaptureTimeoutMs = 2000;
        private BlackBoxStorageSettings _Storage = new BlackBoxStorageSettings();
        private BlackBoxRetentionSettings _Retention = new BlackBoxRetentionSettings();
        private BlackBoxSweeperSettings _Sweeper = new BlackBoxSweeperSettings();
        private string? _SummaryEndpointId = null;
        private int _HotspotReservationThreshold = 3;
        private int _SuccessRetentionDays = 7;
        private int _NearbySuccessWindowHours = 24;
        private int _InsightIntervalSeconds = 60;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate with defaults.
        /// </summary>
        public BlackBoxSettings()
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Copy every value of another settings object into this one, keeping this instance (the black box runtime holds
        /// it, so a settings update must change it in place rather than replace it).
        /// </summary>
        /// <param name="source">Settings to copy.</param>
        /// <exception cref="ArgumentNullException">Thrown when source is null.</exception>
        public void CopyFrom(BlackBoxSettings source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (ReferenceEquals(source, this)) return;
            Enabled = source.Enabled;
            CaptureTimeoutMs = source.CaptureTimeoutMs;
            Storage = source.Storage;
            Retention = source.Retention;
            Sweeper = source.Sweeper;
            SummaryEndpointId = source.SummaryEndpointId;
        }

        #endregion
    }
}
