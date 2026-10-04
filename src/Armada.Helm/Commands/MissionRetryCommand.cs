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
    /// Retry a failed mission (creates a new mission with the same details).
    /// </summary>
    [Description("Retry a failed mission (creates a new copy)")]
    public class MissionRetryCommand : BaseCommand<MissionRetrySettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, MissionRetrySettings settings, CancellationToken cancellationToken)
        {
            // Resolve mission
            Mission? mission = null;
            if (settings.Id.StartsWith("msn_"))
            {
                mission = await GetAsync<Mission>($"/api/v1/missions/{settings.Id}").ConfigureAwait(false);
            }

            if (mission == null)
            {
                EnumerationResult<Mission>? allResult = await GetAsync<EnumerationResult<Mission>>("/api/v1/missions").ConfigureAwait(false);
                List<Mission>? all = allResult?.Objects;
                if (all != null)
                    mission = EntityResolver.ResolveMission(all, settings.Id);
            }

            if (mission == null)
            {
                AnsiConsole.MarkupLine($"[red]Mission not found:[/] {Markup.Escape(settings.Id)}");
                return 1;
            }

            if (mission.Status != MissionStatusEnum.Failed && mission.Status != MissionStatusEnum.Cancelled)
            {
                AnsiConsole.MarkupLine($"[gold1]Mission is {mission.Status}, not Failed or Cancelled.[/] Only failed/cancelled missions can be retried.");
                return 1;
            }

            // Re-create as a new mission with the same details
            Mission? retried = await PostAsync<Mission>("/api/v1/missions", new
            {
                Title = mission.Title,
                Description = mission.Description,
                VesselId = mission.VesselId,
                VoyageId = mission.VoyageId,
                Priority = mission.Priority
            }).ConfigureAwait(false);

            if (retried != null)
            {
                AnsiConsole.MarkupLine($"[green]Mission retried![/]");
                AnsiConsole.MarkupLine($"  [dodgerblue1]Original:[/] [dim]{Markup.Escape(mission.Id)}[/]");
                AnsiConsole.MarkupLine($"  [dodgerblue1]New:[/]      [dim]{Markup.Escape(retried.Id)}[/]");
                AnsiConsole.MarkupLine($"  Run [green]armada watch[/] to monitor progress.");
            }

            return 0;
        }
    }
}
