namespace Armada.Helm.Commands
{
    using System;
    using System.ComponentModel;
    using System.Threading;
    using System.Threading.Tasks;
    using Spectre.Console;
    using Spectre.Console.Cli;
    using Armada.Core.Models;

    /// <summary>
    /// Cancel a fixture battery run (its open mission is cancelled and its dock released).
    /// </summary>
    [Description("Cancel a fixture battery run")]
    public class BatteryCancelCommand : BaseCommand<BatteryCancelSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, BatteryCancelSettings settings, CancellationToken cancellationToken)
        {
            BatteryRunCancelRequest body = new BatteryRunCancelRequest { Reason = String.IsNullOrWhiteSpace(settings.Reason) ? null : settings.Reason.Trim() };
            BatteryRun? run = await PostAsync<BatteryRun>("/api/v1/battery/runs/" + Uri.EscapeDataString(settings.RunId) + "/cancel", body).ConfigureAwait(false);
            if (IsJsonMode(settings))
            {
                WriteJson(run);
                return 0;
            }

            string status = run?.Status.ToString() ?? "unknown";
            AnsiConsole.MarkupLine($"[gold1]Battery run cancel requested:[/] [bold]{Markup.Escape(settings.RunId)}[/] ({Markup.Escape(status)})");
            return 0;
        }
    }
}
