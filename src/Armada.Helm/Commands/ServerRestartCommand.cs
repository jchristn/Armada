namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using System.Net.Http;
    using System.Threading;
    using Spectre.Console;
    using Spectre.Console.Cli;
    using Armada.Core;

    /// <summary>
    /// Restart the Admiral server: stop it if it is running, wait for it to exit, then start it again.
    /// Reuses <see cref="ServerStartCommand"/> for the start half so restart and start stay in lockstep.
    /// </summary>
    [Description("Restart the Admiral server")]
    public class ServerRestartCommand : ServerStartCommand
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, ServerStartSettings settings, CancellationToken cancellationToken)
        {
            await StopRunningServerAsync(cancellationToken).ConfigureAwait(false);
            return await base.ExecuteAsync(context, settings, cancellationToken).ConfigureAwait(false);
        }

        private async Task StopRunningServerAsync(CancellationToken cancellationToken)
        {
            try
            {
                using HttpClient client = CreateAdminHttpClient(TimeSpan.FromSeconds(5));
                await client.PostAsync(GetBaseUrl() + "/api/v1/server/stop", null, cancellationToken).ConfigureAwait(false);
                AnsiConsole.MarkupLine("[green]Stopping Admiral server...[/]");
            }
            catch (HttpRequestException)
            {
                AnsiConsole.MarkupLine("[gold1]Admiral server was not running; starting a fresh instance.[/]");
                return;
            }

            // Wait for the process to fully exit so the port is freed and the executable is unlocked
            // before we start the replacement instance.
            for (int i = 0; i < 15; i++)
            {
                await Task.Delay(1000, cancellationToken).ConfigureAwait(false);

                try
                {
                    using HttpClient pollClient = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
                    await pollClient.GetAsync(GetBaseUrl() + "/api/v1/status/health", cancellationToken).ConfigureAwait(false);
                    // Still responding — keep waiting.
                }
                catch
                {
                    AnsiConsole.MarkupLine("[green]Admiral server stopped.[/]");
                    return;
                }
            }

            AnsiConsole.MarkupLine("[gold1]Server did not stop within the timeout; attempting to start anyway.[/]");
        }
    }
}
