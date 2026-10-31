namespace Armada.Helm.Commands
{
    using System.ComponentModel;
    using System.Threading;
    using Spectre.Console;
    using Spectre.Console.Cli;
    using Armada.Client.Models;
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
            if (settings.MaxFiles.HasValue || settings.MaxSteps.HasValue || settings.MinServedContextTokens.HasValue || (settings.RequiredTools != null && settings.RequiredTools.Length > 0))
            {
                // The server validates the envelope (it may only tighten the vessel default) and answers 400 with the reason.
                mission.WorkerEnvelope = new WorkerEnvelope
                {
                    MaxFiles = settings.MaxFiles,
                    MaxSteps = settings.MaxSteps,
                    MinServedContextTokens = settings.MinServedContextTokens,
                    RequiredTools = settings.RequiredTools != null ? new List<string>(settings.RequiredTools) : new List<string>()
                };
            }

            MissionDispatchResult? result = await CreateMissionAsync(mission).ConfigureAwait(false);
            Mission? created = result?.Mission;

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
            if (!string.IsNullOrEmpty(result!.Warning))
                AnsiConsole.MarkupLine($"  [gold1]Warning:[/] {Markup.Escape(result.Warning)}");
            AnsiConsole.MarkupLine($"  Run [green]armada watch[/] to monitor progress.");
            return 0;
        }
    }
}
