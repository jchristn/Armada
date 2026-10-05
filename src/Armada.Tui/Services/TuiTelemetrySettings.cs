namespace Armada.Tui.Services
{
    using System;
    using Armada.Core.Settings;

    /// <summary>
    /// The <c>Telemetry</c> section of <c>tui.json</c>. Same fields and meaning as the server's <c>Telemetry</c>
    /// settings (<see cref="TelemetrySettings"/>), with TUI defaults: disabled, service name <c>armada-tui</c>, and no
    /// Prometheus scrape endpoint (a TUI is short-lived, so OTLP push is the usual path; a scrape endpoint must not
    /// collide with the Admiral's 9464).
    /// </summary>
    public class TuiTelemetrySettings
    {
        #region Public-Members

        /// <summary>
        /// Whether telemetry export is enabled. Default false: no exporter starts and the TUI and TUIKit instruments are
        /// switched off.
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// Logical service name reported to the telemetry backend. Never null or empty.
        /// </summary>
        public string ServiceName
        {
            get { return _ServiceName; }
            set { _ServiceName = String.IsNullOrWhiteSpace(value) ? "armada-tui" : value; }
        }

        /// <summary>
        /// Optional OTLP collector endpoint (for example <c>http://127.0.0.1:4317</c>), or null.
        /// </summary>
        public string? OtlpEndpoint { get; set; } = null;

        /// <summary>
        /// Whether to serve an in-process Prometheus scrape endpoint while the TUI runs. Default false.
        /// </summary>
        public bool PrometheusEnabled { get; set; } = false;

        /// <summary>
        /// TCP port for the Prometheus scrape endpoint. Clamped to [1, 65535]. Default 9465.
        /// </summary>
        public int PrometheusPort
        {
            get { return _PrometheusPort; }
            set { _PrometheusPort = Math.Clamp(value, 1, 65535); }
        }

        /// <summary>
        /// Optional Loki push endpoint (for example <c>http://127.0.0.1:3100</c>), or null.
        /// </summary>
        public string? LokiEndpoint { get; set; } = null;

        #endregion

        #region Private-Members

        private string _ServiceName = "armada-tui";
        private int _PrometheusPort = 9465;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate with defaults.
        /// </summary>
        public TuiTelemetrySettings()
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The equivalent server-side settings object, for the shared telemetry host.
        /// </summary>
        /// <returns>Settings.</returns>
        public TelemetrySettings ToTelemetrySettings()
        {
            TelemetrySettings settings = new TelemetrySettings();
            settings.Enabled = Enabled;
            settings.ServiceName = ServiceName;
            settings.OtlpEndpoint = String.IsNullOrWhiteSpace(OtlpEndpoint) ? null : OtlpEndpoint!.Trim();
            settings.PrometheusEnabled = PrometheusEnabled;
            settings.PrometheusPort = PrometheusPort;
            settings.LokiEndpoint = String.IsNullOrWhiteSpace(LokiEndpoint) ? null : LokiEndpoint!.Trim();
            return settings;
        }

        #endregion
    }
}
