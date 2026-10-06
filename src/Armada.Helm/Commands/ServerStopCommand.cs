namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using System.Diagnostics;
    using System.Net;
    using System.Net.Http;
    using System.Runtime.InteropServices;
    using System.Threading;
    using Spectre.Console;
    using Spectre.Console.Cli;
    using Armada.Core;
    using Armada.Helm.Infrastructure;

    /// <summary>
    /// Stop the Admiral server: the local one, or a remote one through <c>POST /api/v1/server/stop</c> (global admin).
    /// </summary>
    [Description("Stop the Admiral server")]
    public class ServerStopCommand : BaseCommand<ServerStopSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, ServerStopSettings settings, CancellationToken cancellationToken)
        {
            AdmiralTarget target = GetTarget();
            try
            {
                using HttpClient client = CreateAdminHttpClient(TimeSpan.FromSeconds(5));
                HttpResponseMessage response = await client.PostAsync(GetBaseUrl() + "/api/v1/server/stop", null, cancellationToken).ConfigureAwait(false);
                if (!target.IsLocal && (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden))
                    throw CredentialError(target, response.StatusCode, "POST /api/v1/server/stop");
                if (!response.IsSuccessStatusCode)
                {
                    AnsiConsole.MarkupLine("[red]The Admiral refused to stop:[/] HTTP " + (int)response.StatusCode + " from " + Markup.Escape(target.Describe()));
                    return 1;
                }

                AnsiConsole.MarkupLine("[green]Admiral server is shutting down...[/]" + (target.IsLocal ? "" : " [dim](" + Markup.Escape(target.Describe()) + ")[/]"));
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
                    // Still responding -- keep waiting
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

            if (!target.IsLocal)
                AnsiConsole.MarkupLine("[dim]A remote Admiral run as a service may be restarted by its service manager; start it again on its host otherwise.[/]");

            return 0;
        }
    }
}
