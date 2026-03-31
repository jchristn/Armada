namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using System.Diagnostics;
    using System.Net.Http;
    using System.Runtime.InteropServices;
    using System.Threading;
    using Spectre.Console;
    using Spectre.Console.Cli;
    using Armada.Core;

    /// <summary>
    /// Stop the Admiral server.
    /// </summary>
    [Description("Stop the Admiral server")]
    public class ServerStopCommand : BaseCommand<ServerStopSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, ServerStopSettings settings, CancellationToken cancellationToken)
        {
            try
            {
                using HttpClient client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                await client.PostAsync(GetBaseUrl() + "/api/v1/server/stop", null, cancellationToken).ConfigureAwait(false);
                AnsiConsole.MarkupLine("[green]Admiral server is shutting down...[/]");
            }
            catch (HttpRequestException)
            {
                AnsiConsole.MarkupLine("[gold1]Admiral server is not reachable (may already be stopped).[/]");
                return 1;
            }

            // Wait for the process to fully exit so the exe is unlocked for subsequent builds
            bool exited = false;
            for (int i = 0; i < 15; i++)
            {
                await Task.Delay(1000, cancellationToken).ConfigureAwait(false);

                try
                {
                    using HttpClient pollClient = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
                    await pollClient.GetAsync(GetBaseUrl() + "/api/v1/status/health", cancellationToken).ConfigureAwait(false);
                    // Still responding — keep waiting
                }
                catch
                {
                    exited = true;
                    break;
                }
            }

            if (exited)
                AnsiConsole.MarkupLine("[green]Admiral server stopped.[/]");
            else
                AnsiConsole.MarkupLine("[gold1]Server is still shutting down. Wait a moment before restarting.[/]");

            return 0;
        }
    }
}
