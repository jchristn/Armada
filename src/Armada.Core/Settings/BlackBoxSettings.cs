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

        #endregion

        #region Private-Members

        private int _CaptureTimeoutMs = 2000;
        private BlackBoxStorageSettings _Storage = new BlackBoxStorageSettings();
        private BlackBoxRetentionSettings _Retention = new BlackBoxRetentionSettings();
        private BlackBoxSweeperSettings _Sweeper = new BlackBoxSweeperSettings();
        private string? _SummaryEndpointId = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate with defaults.
        /// </summary>
        public BlackBoxSettings()
        {
        }

        #endregion
    }
}
