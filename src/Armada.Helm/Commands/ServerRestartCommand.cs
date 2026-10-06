namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using System.Net;
    using System.Net.Http;
    using System.Threading;
    using Spectre.Console;
    using Spectre.Console.Cli;
    using Armada.Core;
    using Armada.Helm.Infrastructure;

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
            AdmiralTarget target = GetTarget();
            if (!target.IsLocal) return await RestartRemoteAsync(target, cancellationToken).ConfigureAwait(false);
            await StopRunningServerAsync(cancellationToken).ConfigureAwait(false);
            return await base.ExecuteAsync(context, settings, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Restart acts on a remote Admiral through POST /api/v1/server/restart, so it is not local-only (start is).
        /// </summary>
        protected override LocalOnlyCommandInfo? LocalOnly
        {
            get { return null; }
        }

        private async Task<int> RestartRemoteAsync(AdmiralTarget target, CancellationToken cancellationToken)
        {
            using (HttpClient client = CreateAdminHttpClient(TimeSpan.FromSeconds(10)))
            {
                HttpResponseMessage response;
                try
                {
                    response = await client.PostAsync(target.BaseUrl + "/api/v1/server/restart", null, cancellationToken).ConfigureAwait(false);
                }
                catch (HttpRequestException)
                {
                    throw new AdmiralTargetException(AdmiralTargetErrorEnum.Unreachable, "Cannot reach the Admiral at " + target.Describe() + "; nothing was restarted.");
                }

                if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
                    throw CredentialError(target, response.StatusCode, "POST /api/v1/server/restart");
                if (!response.IsSuccessStatusCode)
                {
                    AnsiConsole.MarkupLine("[red]The Admiral could not restart:[/] HTTP " + (int)response.StatusCode + " " + Markup.Escape(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)));
                    return 1;
                }
            }

            AnsiConsole.MarkupLine("[green]Restart requested[/] on " + Markup.Escape(target.Describe()) + "; waiting for it to answer again...");
            using HttpClient poll = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            RestartWaitResultEnum result = await WaitForRestartAsync(
                async token =>
                {
                    try
                    {
                        return (await poll.GetAsync(target.BaseUrl + "/api/v1/status/health", token).ConfigureAwait(false)).IsSuccessStatusCode;
                    }
                    catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException)
                    {
                        return false;
                    }
                },
                TimeSpan.FromSeconds(60),
                TimeSpan.FromSeconds(1),
                TimeProvider.System,
                cancellationToken).ConfigureAwait(false);

            if (result == RestartWaitResultEnum.BackUp)
            {
                AnsiConsole.MarkupLine("[green]Admiral is back up.[/]");
                return 0;
            }

            AnsiConsole.MarkupLine(result == RestartWaitResultEnum.DidNotReturn
                ? "[gold1]The Admiral stopped but did not answer again within 60 seconds; check it on its host.[/]"
                : "[gold1]The Admiral kept answering; the restart may not have happened. Check admiral.log on its host.[/]");
            return 1;
        }

        /// <summary>
        /// Poll until the Admiral has gone down and answered again, or the timeout elapses. The timeout is measured on
        /// the monotonic clock of <paramref name="time"/>: a wall-clock deadline expired early when the clock jumped (the
        /// laptop running the CLI slept and woke).
        /// </summary>
        /// <param name="isUp">Health probe; true when the Admiral answers.</param>
        /// <param name="timeout">How long to wait.</param>
        /// <param name="interval">Delay before each probe.</param>
        /// <param name="time">Time source.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The outcome.</returns>
        internal static async Task<RestartWaitResultEnum> WaitForRestartAsync(
            Func<CancellationToken, Task<bool>> isUp,
            TimeSpan timeout,
            TimeSpan interval,
            TimeProvider time,
            CancellationToken token = default)
        {
            if (isUp == null) throw new ArgumentNullException(nameof(isUp));
            if (time == null) throw new ArgumentNullException(nameof(time));

            long started = time.GetTimestamp();
            bool wentDown = false;
            while (time.GetElapsedTime(started) < timeout)
            {
                await Task.Delay(interval, token).ConfigureAwait(false);
                bool up = await isUp(token).ConfigureAwait(false);
                if (!up) wentDown = true;
                else if (wentDown) return RestartWaitResultEnum.BackUp;
            }

            return wentDown ? RestartWaitResultEnum.DidNotReturn : RestartWaitResultEnum.NeverWentDown;
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
