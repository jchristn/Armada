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
    /// Stop a captain by name or ID.
    /// </summary>
    [Description("Stop a captain")]
    public class CaptainStopCommand : BaseCommand<CaptainStopSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, CaptainStopSettings settings, CancellationToken cancellationToken)
        {
            // Resolve by name or ID
            string captainId = settings.Id;
            if (!captainId.StartsWith("cpt_"))
            {
                EnumerationResult<Captain>? captainResult = await GetAsync<EnumerationResult<Captain>>("/api/v1/captains").ConfigureAwait(false);
                List<Captain>? captains = captainResult?.Objects;
                if (captains != null)
                {
                    Captain? match = EntityResolver.ResolveCaptain(captains, settings.Id);
                    if (match != null)
                    {
                        captainId = match.Id;
                    }
                    else
                    {
                        AnsiConsole.MarkupLine($"[red]Captain not found:[/] {Markup.Escape(settings.Id)}");
                        if (captains.Count > 0)
                        {
                            AnsiConsole.MarkupLine("[dim]Available captains:[/]");
                            foreach (Captain c in captains)
                            {
                                AnsiConsole.MarkupLine($"  [dim]-[/] [bold]{Markup.Escape(c.Name)}[/] [dim]({Markup.Escape(c.Id)})[/] [{TableRenderer.CaptainStateColor(c.State)}]{c.State}[/]");
                            }
                        }
                        return 1;
                    }
                }
            }

            await PostAsync($"/api/v1/captains/{captainId}/stop").ConfigureAwait(false);
            AnsiConsole.MarkupLine($"[gold1]Recall signal sent to captain:[/] [bold]{Markup.Escape(settings.Id)}[/]");
            return 0;
        }
    }
}
