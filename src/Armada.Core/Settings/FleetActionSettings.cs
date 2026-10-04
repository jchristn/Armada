namespace Armada.Core.Settings
{
    /// <summary>
    /// Settings for fleet actions (one action applied across many vessels). All values apply live.
    /// </summary>
    public class FleetActionSettings
    {
        #region Public-Members

        /// <summary>
        /// Upper bound on any run's concurrency. Default 8, minimum 1, maximum 32; out-of-range values are clamped.
        /// </summary>
        public int MaxConcurrency
        {
            get => _MaxConcurrency;
            set => _MaxConcurrency = value < 1 ? 1 : (value > 32 ? 32 : value);
        }

        /// <summary>
        /// Default per-target command timeout in seconds. Default 300, minimum 5, maximum 7200; out-of-range values
        /// are clamped.
        /// </summary>
        public int DefaultTimeoutSeconds
        {
            get => _DefaultTimeoutSeconds;
            set => _DefaultTimeoutSeconds = value < 5 ? 5 : (value > 7200 ? 7200 : value);
        }

        /// <summary>
        /// Maximum bytes of standard output and of standard error kept per target; longer output is truncated and
        /// flagged. Default 65536, minimum 1024, maximum 1048576; out-of-range values are clamped.
        /// </summary>
        public int MaxOutputBytes
        {
            get => _MaxOutputBytes;
            set => _MaxOutputBytes = value < 1024 ? 1024 : (value > 1048576 ? 1048576 : value);
        }

        /// <summary>
        /// Days a finished run (and its targets) is retained before pruning. Default 30, minimum 1, maximum 3650;
        /// out-of-range values are clamped.
        /// </summary>
        public int RunRetentionDays
        {
            get => _RunRetentionDays;
            set => _RunRetentionDays = value < 1 ? 1 : (value > 3650 ? 3650 : value);
        }

        #endregion

        #region Private-Members

        private int _MaxConcurrency = 8;
        private int _DefaultTimeoutSeconds = 300;
        private int _MaxOutputBytes = 65536;
        private int _RunRetentionDays = 30;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate with defaults.
        /// </summary>
        public FleetActionSettings()
        {
        }

        #endregion
    }
}
