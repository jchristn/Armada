namespace Armada.Helm.Commands
{
    using System;
    using System.ComponentModel;
    using System.Threading;
    using System.Threading.Tasks;
    using Spectre.Console.Cli;
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
            bool explicitServer = !String.IsNullOrWhiteSpace(settings.Server)
                || !String.IsNullOrWhiteSpace(settings.Profile)
                || !String.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(TuiPaths.ServerUrlEnvironmentVariable));
            if (!explicitServer) options.DefaultServerUrl = GetBaseUrl();
            return await ArmadaTuiApp.RunConsoleAsync(options, cancellationToken).ConfigureAwait(false);
        }
    }
}
