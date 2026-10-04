namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using System.Threading;
    using System.Threading.Tasks;
    using Spectre.Console;
    using Spectre.Console.Cli;
    using Armada.Core.Models;

    /// <summary>
    /// Cancel a fleet action run.
    /// </summary>
    [Description("Cancel a fleet action run")]
    public class ActionCancelCommand : BaseCommand<ActionRunIdSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, ActionRunIdSettings settings, CancellationToken cancellationToken)
        {
            FleetActionRun? run = await PostAsync<FleetActionRun>("/api/v1/fleet-action-runs/" + settings.RunId + "/cancel", new { }).ConfigureAwait(false);
            if (IsJsonMode(settings))
            {
                WriteJson(run);
                return 0;
            }

            AnsiConsole.MarkupLine($"[gold1]Run cancelled:[/] [bold]{Markup.Escape(settings.RunId)}[/]");
            return 0;
        }
    }
}
