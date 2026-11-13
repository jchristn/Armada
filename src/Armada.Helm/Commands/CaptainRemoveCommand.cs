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
    using Armada.Helm.Infrastructure;

    /// <summary>
    /// Remove a captain. Accepts name or ID.
    /// </summary>
    [Description("Remove a captain")]
    public class CaptainRemoveCommand : BaseCommand<CaptainRemoveSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, CaptainRemoveSettings settings, CancellationToken cancellationToken)
        {
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
                        HelmConsole.Error.MarkupLine($"[red]Captain not found:[/] {Markup.Escape(settings.Id)}");
                        return 1;
                    }
                }
            }

            await DeleteAsync($"/api/v1/captains/{captainId}").ConfigureAwait(false);
            AnsiConsole.MarkupLine($"[gold1]Captain removed:[/] [bold]{Markup.Escape(settings.Id)}[/]");
            return 0;
        }
    }
}
