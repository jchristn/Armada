namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using System.Threading;
    using Spectre.Console;
    using Spectre.Console.Cli;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Helm.Rendering;

    /// <summary>
    /// Remove a fleet.
    /// </summary>
    [Description("Remove a fleet")]
    public class FleetRemoveCommand : BaseCommand<FleetRemoveSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, FleetRemoveSettings settings, CancellationToken cancellationToken)
        {
            string fleetId = settings.Id;
            if (!fleetId.StartsWith("flt_"))
            {
                EnumerationResult<Fleet>? fleetResult = await GetAsync<EnumerationResult<Fleet>>("/api/v1/fleets").ConfigureAwait(false);
                List<Fleet>? fleets = fleetResult?.Objects;
                if (fleets != null)
                {
                    Fleet? match = EntityResolver.ResolveFleet(fleets, settings.Id);
                    if (match != null)
                    {
                        fleetId = match.Id;
                    }
                    else
                    {
                        AnsiConsole.MarkupLine($"[red]Fleet not found:[/] {Markup.Escape(settings.Id)}");
                        return 1;
                    }
                }
            }

            await DeleteAsync($"/api/v1/fleets/{fleetId}").ConfigureAwait(false);
            AnsiConsole.MarkupLine($"[gold1]Fleet removed:[/] [bold]{Markup.Escape(settings.Id)}[/]");
            return 0;
        }
    }
}
