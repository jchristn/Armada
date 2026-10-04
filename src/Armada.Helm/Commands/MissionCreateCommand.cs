namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using System.Threading;
    using Spectre.Console;
    using Spectre.Console.Cli;
    using Armada.Core;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Helm.Rendering;

    /// <summary>
    /// Create a standalone mission.
    /// </summary>
    [Description("Create a new mission")]
    public class MissionCreateCommand : BaseCommand<MissionCreateSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, MissionCreateSettings settings, CancellationToken cancellationToken)
        {
            Mission mission = new Mission(settings.Title, settings.Description);

            // Resolve vessel by name or ID
            if (!string.IsNullOrEmpty(settings.Vessel))
            {
                EnumerationResult<Vessel>? vesselResult = await GetAsync<EnumerationResult<Vessel>>("/api/v1/vessels").ConfigureAwait(false);
                List<Vessel>? vessels = vesselResult?.Objects;
                if (vessels != null)
                {
                    Vessel? match = EntityResolver.ResolveVessel(vessels, settings.Vessel);
                    if (match != null) mission.VesselId = match.Id;
                    else mission.VesselId = settings.Vessel; // Pass raw, let server validate
                }
            }

            if (!string.IsNullOrEmpty(settings.Voyage)) mission.VoyageId = settings.Voyage;
            if (settings.Priority.HasValue) mission.Priority = settings.Priority.Value;

            Mission? created = await PostAsync<Mission>("/api/v1/missions", mission).ConfigureAwait(false);

            if (created == null)
            {
                AnsiConsole.MarkupLine("[red]Failed to create mission.[/]");
                return 1;
            }

            AnsiConsole.MarkupLine($"[green]Mission created![/]");
            AnsiConsole.MarkupLine($"  [dodgerblue1]Id:[/]     [dim]{Markup.Escape(created.Id)}[/]");
            AnsiConsole.MarkupLine($"  [dodgerblue1]Title:[/]  [bold]{Markup.Escape(created.Title)}[/]");
            AnsiConsole.MarkupLine($"  [dodgerblue1]Status:[/] {created.Status}");
            if (!string.IsNullOrEmpty(created.CaptainId))
                AnsiConsole.MarkupLine($"  [dodgerblue1]Captain:[/] {Markup.Escape(created.CaptainId)}");
            AnsiConsole.MarkupLine($"  Run [green]armada watch[/] to monitor progress.");
            return 0;
        }
    }
}
