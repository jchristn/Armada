namespace Armada.Core.Settings
{
    using Armada.Core.Models;

    /// <summary>
    /// Model alert settings (docs/MODEL_HETEROGENEITY.md, Alerts): the periodic evaluation pass, the optional push, and
    /// the thresholds. Alerts are advisory and never change captains, personas, templates or routing. All values apply
    /// live.
    /// </summary>
    public class ModelAlertSettings
    {
        #region Public-Members

        /// <summary>
        /// Whether the periodic evaluation pass runs. Default true. On-demand evaluation works either way.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Minutes between periodic passes. Default 60, clamped to 5..10080.
        /// </summary>
        public int IntervalMinutes
        {
            get => _IntervalMinutes;
            set => _IntervalMinutes = value < 5 ? 5 : (value > 10080 ? 10080 : value);
        }

        /// <summary>
        /// Whether a newly raised or reopened alert is also pushed to the tenant's admins (category ModelAlert). Default
        /// false.
        /// </summary>
        public bool PushEnabled { get; set; } = false;

        /// <summary>
        /// Whether each vessel with recent attempts is also evaluated on its own, in addition to all vessels together.
        /// Default false.
        /// </summary>
        public bool IncludeVessels { get; set; } = false;

        /// <summary>
        /// Days an Ignore hides an alert when the request gives none. Default 7, clamped to 1..365.
        /// </summary>
        public int DefaultIgnoreDays
        {
            get => _DefaultIgnoreDays;
            set => _DefaultIgnoreDays = value < 1 ? 1 : (value > 365 ? 365 : value);
        }

        /// <summary>
        /// Thresholds: defaults and per-persona overrides. Never null.
        /// </summary>
        public ModelAlertThresholds Thresholds
        {
            get => _Thresholds;
            set => _Thresholds = value ?? new ModelAlertThresholds();
        }

        #endregion

        #region Private-Members

        private int _IntervalMinutes = 60;
        private int _DefaultIgnoreDays = 7;
        private ModelAlertThresholds _Thresholds = new ModelAlertThresholds();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate with defaults.
        /// </summary>
        public ModelAlertSettings()
        {
        }

        #endregion
    }
}
