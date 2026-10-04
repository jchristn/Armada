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
    /// Check Admiral server health.
    /// </summary>
    [Description("Check Admiral server status")]
    public class ServerStatusCommand : BaseCommand<ServerStatusSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, ServerStatusSettings settings, CancellationToken cancellationToken)
        {
            try
            {
                using HttpClient client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                HttpResponseMessage resp = await client.GetAsync(GetBaseUrl() + "/api/v1/status/health", cancellationToken).ConfigureAwait(false);
                if (resp.IsSuccessStatusCode)
                {
                    AnsiConsole.MarkupLine("[green]Admiral server is running![/]");
                    AnsiConsole.MarkupLine("[dodgerblue1]Health:[/] healthy");
                }
                else
                {
                    AnsiConsole.MarkupLine("[red]Admiral server returned unhealthy status.[/]");
                    return 1;
                }
            }
            catch (HttpRequestException)
            {
                AnsiConsole.MarkupLine("[red]Admiral server is not reachable.[/]");
                AnsiConsole.MarkupLine($"[dim]  Tried: {GetBaseUrl()}[/]");
                return 1;
            }

            return 0;
        }
    }
}
