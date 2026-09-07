namespace Armada.Server
{
    using System;
    using Microsoft.Extensions.Logging;
    using Radiant;
    using SyslogLogging;
    using Armada.Core;
    using Armada.Core.Settings;

    /// <summary>
    /// Owns the OpenTelemetry pipeline for the Admiral. When telemetry is enabled it starts a Radiant
    /// host that observes Armada's meter (<see cref="ArmadaMetrics.MeterName"/>) plus the web server and
    /// HTTP client instrumentation, exporting to an OTLP collector, an in-process Prometheus scrape
    /// endpoint, and/or Loki per <see cref="TelemetrySettings"/>. This is the one composition-root type
    /// that depends on Radiant; the rest of Armada emits through the base class library only.
    /// </summary>
    public class ArmadaTelemetryHost : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// True when a telemetry host is running.
        /// </summary>
        public bool IsRunning => _Host != null;

        #endregion

        #region Private-Members

        private readonly LoggingModule _Logging;
        private readonly string _Header = "[ArmadaTelemetryHost] ";
        private RadiantHost? _Host = null;

        // When log export is active, we forward the SyslogLogging stream into a Radiant ILogger so every
        // _Logging.* line (including full-stack exception logs) is shipped to Loki/OTLP alongside the
        // console/file/syslog sinks. Held so we can unsubscribe on dispose.
        private Microsoft.Extensions.Logging.ILogger? _LokiLogger = null;
        private Action<LogEntry>? _MessageForwarder = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate. The host is not started until <see cref="Start(TelemetrySettings)"/> is called.
        /// </summary>
        /// <param name="logging">Logging module. Required.</param>
        public ArmadaTelemetryHost(LoggingModule logging)
        {
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Start the telemetry host from the supplied settings. A no-op when settings are null or
        /// <see cref="TelemetrySettings.Enabled"/> is false, or when a host is already running.
        /// Failures are logged and swallowed so telemetry never blocks Admiral startup.
        /// </summary>
        /// <param name="settings">Telemetry settings.</param>
        public void Start(TelemetrySettings settings)
        {
            if (settings == null || !settings.Enabled) return;
            if (_Host != null) return;

            try
            {
                RadiantSettings radiant = new RadiantSettings(settings.ServiceName);
                radiant.DiagnosticCallback = message => _Logging.Debug(_Header + message);

                if (!String.IsNullOrWhiteSpace(settings.OtlpEndpoint))
                {
                    radiant.Otlp.Endpoint = settings.OtlpEndpoint;
                }
                else
                {
                    // OTLP push defaults on; disable it when no collector is configured so we do not
                    // spam a nonexistent default endpoint.
                    radiant.Otlp.Enable = false;
                }

                radiant.Prometheus.Enable = settings.PrometheusEnabled;
                if (settings.PrometheusEnabled)
                {
                    radiant.Prometheus.Port = settings.PrometheusPort;
                }

                if (!String.IsNullOrWhiteSpace(settings.LokiEndpoint))
                {
                    radiant.Loki.Enable = true;
                    radiant.Loki.Endpoint = settings.LokiEndpoint;
                }

                // Export logs (not just metrics/traces) when a Loki or OTLP sink is configured, so the
                // Admiral's own log stream -- especially exception traces -- lands in Loki for debugging.
                bool exportLogs = !String.IsNullOrWhiteSpace(settings.LokiEndpoint) || !String.IsNullOrWhiteSpace(settings.OtlpEndpoint);
                radiant.Logs.Enable = exportLogs;

                // Armada's own instruments, plus the web server and HTTP client instrumentation.
                // Subscribing to a name that emits nothing is harmless.
                radiant.Sources.AddMeter(ArmadaMetrics.MeterName);
                radiant.Sources.AddActivitySource(ArmadaMetrics.MeterName);
                radiant.Sources.AddMeter("Watson");
                radiant.Sources.AddMeter("Microsoft.AspNetCore.Hosting");
                radiant.Sources.AddMeter("System.Net.Http");

                _Host = RadiantHost.Start(radiant);

                if (exportLogs && _Host.LoggerFactory != null)
                {
                    _LokiLogger = _Host.LoggerFactory.CreateLogger("Armada.Admiral");
                    _MessageForwarder = ForwardLogEntry;
                    _Logging.MessageLogged += _MessageForwarder;
                    _Logging.Info(_Header + "bridging the Admiral log stream to the telemetry log exporter (Loki/OTLP)");
                }

                _Logging.Info(_Header + "telemetry host started for service '" + settings.ServiceName + "'" +
                    (settings.PrometheusEnabled ? " (Prometheus scrape " + radiant.Prometheus.ToScrapeUrl() + ")" : "") +
                    (!String.IsNullOrWhiteSpace(settings.OtlpEndpoint) ? " (OTLP " + settings.OtlpEndpoint + ")" : ""));
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "failed to start telemetry host: " + ex.ToString());
                _Host = null;
            }
        }

        /// <summary>
        /// Forward one SyslogLogging entry into the Radiant log exporter (Loki/OTLP). Info and above only, so
        /// Debug chatter (including Radiant's own diagnostic callback, which logs at Debug) is not shipped and
        /// cannot form a feedback loop. The raw message is passed as a single structured value so it is never
        /// parsed as a message template.
        /// </summary>
        private void ForwardLogEntry(LogEntry entry)
        {
            if (entry == null) return;
            Microsoft.Extensions.Logging.ILogger? logger = _LokiLogger;
            if (logger == null) return;
            if (entry.Severity == Severity.Debug) return;

            try
            {
                LogLevel level = ToLogLevel(entry.Severity);
                logger.Log(level, entry.Exception, "{ArmadaMessage}", entry.Message ?? String.Empty);
            }
            catch
            {
                // Never let log forwarding throw back into the caller's logging path.
            }
        }

        private static LogLevel ToLogLevel(Severity severity)
        {
            switch (severity)
            {
                case Severity.Debug: return LogLevel.Debug;
                case Severity.Info: return LogLevel.Information;
                case Severity.Warn: return LogLevel.Warning;
                case Severity.Error: return LogLevel.Error;
                case Severity.Alert:
                case Severity.Critical:
                case Severity.Emergency: return LogLevel.Critical;
                default: return LogLevel.Information;
            }
        }

        /// <summary>
        /// Dispose the telemetry host, flushing pending telemetry.
        /// </summary>
        public void Dispose()
        {
            try
            {
                if (_MessageForwarder != null)
                {
                    _Logging.MessageLogged -= _MessageForwarder;
                    _MessageForwarder = null;
                }
                _LokiLogger = null;
                _Host?.Dispose();
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "error disposing telemetry host: " + ex.ToString());
            }
            finally
            {
                _Host = null;
            }

            GC.SuppressFinalize(this);
        }

        #endregion
    }
}
