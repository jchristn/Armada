namespace Armada.Helm.Commands
{
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Threading;
    using Spectre.Console;
    using Spectre.Console.Cli;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Helm.Rendering;

    /// <summary>
    /// Stop all captains.
    /// </summary>
    [Description("Stop all captains")]
    public class CaptainStopAllCommand : BaseCommand<CaptainStopAllSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, CaptainStopAllSettings settings, CancellationToken cancellationToken)
        {
            if (!AnsiConsole.Confirm("[bold red]RECALL ALL CAPTAINS?[/] This will stop all active agents.", defaultValue: false))
            {
                AnsiConsole.MarkupLine("[dodgerblue1]Cancelled. Fleet remains operational.[/]");
                return 0;
            }

            EnumerationResult<Captain>? captainResult = await GetAsync<EnumerationResult<Captain>>("/api/v1/captains").ConfigureAwait(false);
            List<Captain>? captains = captainResult?.Objects;

            if (captains == null || captains.Count == 0)
            {
                AnsiConsole.MarkupLine("[gold1]No captains to recall.[/]");
                return 0;
            }

            int stopped = 0;
            foreach (Captain captain in captains)
            {
                try
                {
                    await PostAsync($"/api/v1/captains/{captain.Id}/stop").ConfigureAwait(false);
                    AnsiConsole.MarkupLine($"  [gold1]Recalled:[/] [bold]{Markup.Escape(captain.Name)}[/] [dim]({Markup.Escape(captain.Id)})[/]");
                    stopped++;
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"  [red]Failed:[/] {Markup.Escape(captain.Id)} -- {Markup.Escape(ex.Message)}");
                }
            }

            AnsiConsole.MarkupLine($"\n[green]Recall signal sent to {stopped} captain(s).[/]");
            return 0;
        }
    }
}
