namespace Armada.Helm.Commands
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Threading;
    using System.Threading.Tasks;
    using Spectre.Console.Cli;
    using SyslogLogging;
    using TUIKit.Diagnostics;
    using Armada.Core.Settings;
    using Armada.Helm.Infrastructure;
    using Armada.Server;
    using Armada.Tui;
    using Armada.Tui.Services;

    /// <summary>
    /// Start the Armada terminal UI (the dashboard in a terminal), hosted in-process (Decision D1).
    /// </summary>
    [Description("Open the Armada terminal UI")]
    public class TuiCommand : BaseCommand<TuiSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, TuiSettings settings, CancellationToken cancellationToken)
        {
            TuiStartOptions options = new TuiStartOptions();
            options.ServerUrl = settings.Server;
            options.ProfileName = settings.Profile;
            options.StartRoute = settings.Route;
            options.Token = String.IsNullOrWhiteSpace(settings.Token) ? null : settings.Token.Trim();

            // The CLI's ARMADA_SERVER_URL selects the TUI's server too (before the TUI's own ARMADA_URL), unless
            // --profile names a saved profile.
            string? envServer = Environment.GetEnvironmentVariable(AdmiralTargetResolver.ServerUrlEnvironmentVariable);
            if (String.IsNullOrWhiteSpace(options.ServerUrl) && String.IsNullOrWhiteSpace(options.ProfileName) && !String.IsNullOrWhiteSpace(envServer))
                options.ServerUrl = AdmiralTargetResolver.NormalizeServerUrl(envServer, AdmiralTargetResolver.ServerUrlEnvironmentVariable);

            bool explicitServer = !String.IsNullOrWhiteSpace(options.ServerUrl)
                || !String.IsNullOrWhiteSpace(settings.Profile)
                || !String.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(TuiPaths.ServerUrlEnvironmentVariable));
            if (!explicitServer) options.DefaultServerUrl = GetLocalBaseUrl();
            options.TelemetryHostFactory = StartTelemetryHost;
            return await ArmadaTuiApp.RunConsoleAsync(options, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Start the Admiral's telemetry host for the TUI process, observing the TUI's and TUIKit's meters and
        /// activity sources. Logs go nowhere visible (the TUI owns the terminal).
        /// </summary>
        /// <param name="telemetry">Settings from the <c>Telemetry</c> section of <c>tui.json</c>.</param>
        /// <returns>The running host, or null when it did not start.</returns>
        public static IDisposable? StartTelemetryHost(TelemetrySettings telemetry)
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            ArmadaTelemetryHost host = new ArmadaTelemetryHost(logging);
            host.AdditionalSources = new List<string> { TuiTelemetry.MeterName, TuiKitTelemetryNames.MeterName };
            host.Start(telemetry);
            if (host.IsRunning) return host;
            host.Dispose();
            return null;
        }
    }
}
