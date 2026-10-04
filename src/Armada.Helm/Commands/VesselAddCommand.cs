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
    /// Add a new vessel.
    /// </summary>
    [Description("Add a new vessel")]
    public class VesselAddCommand : BaseCommand<VesselAddSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, VesselAddSettings settings, CancellationToken cancellationToken)
        {
            // Resolve fleet by name if provided
            string? fleetId = settings.Fleet;
            if (!string.IsNullOrEmpty(fleetId) && !fleetId.StartsWith("flt_"))
            {
                EnumerationResult<Fleet>? fleetResult = await GetAsync<EnumerationResult<Fleet>>("/api/v1/fleets").ConfigureAwait(false);
                List<Fleet>? fleets = fleetResult?.Objects;
                if (fleets != null)
                {
                    Fleet? match = EntityResolver.ResolveFleet(fleets, fleetId);
                    if (match != null) fleetId = match.Id;
                }
            }

            object body = new
            {
                Name = settings.Name,
                RepoUrl = settings.RepoUrl,
                FleetId = fleetId,
                DefaultBranch = settings.Branch ?? "main"
            };

            Vessel? vessel = await PostAsync<Vessel>("/api/v1/vessels", body).ConfigureAwait(false);

            if (vessel != null)
            {
                AnsiConsole.MarkupLine($"[green]Vessel registered![/] [bold]{Markup.Escape(vessel.Name)}[/] [dim]({Markup.Escape(vessel.Id)})[/]");
                AnsiConsole.MarkupLine($"  Dispatch work with [green]armada go \"your task\" --vessel {Markup.Escape(vessel.Name)}[/].");
            }

            return 0;
        }
    }
}
