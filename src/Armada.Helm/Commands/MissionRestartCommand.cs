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
    /// Restart a failed or cancelled mission, resetting it to Pending.
    /// </summary>
    [Description("Restart a failed or cancelled mission")]
    public class MissionRestartCommand : BaseCommand<MissionRestartSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, MissionRestartSettings settings, CancellationToken cancellationToken)
        {
            string missionId = await ResolveMissionIdAsync(settings.Id).ConfigureAwait(false);

            Mission? mission = await GetAsync<Mission>($"/api/v1/missions/{missionId}").ConfigureAwait(false);
            if (mission == null)
            {
                AnsiConsole.MarkupLine($"[red]Mission not found:[/] {Markup.Escape(missionId)}");
                return 1;
            }

            if (mission.Status != MissionStatusEnum.Failed && mission.Status != MissionStatusEnum.Cancelled)
            {
                AnsiConsole.MarkupLine($"[gold1]Mission is {mission.Status}, not Failed or Cancelled.[/] Only failed/cancelled missions can be restarted.");
                return 1;
            }

            string title = settings.Title ?? mission.Title;
            string description = settings.Description ?? mission.Description ?? "";

            AnsiConsole.MarkupLine($"[dodgerblue1]Mission:[/]  {Markup.Escape(missionId)}");
            AnsiConsole.MarkupLine($"[dodgerblue1]Title:[/]    {Markup.Escape(title)}");
            if (title != mission.Title)
                AnsiConsole.MarkupLine($"[dim]  (was: {Markup.Escape(mission.Title)})[/]");

            if (!AnsiConsole.Confirm("Restart this mission?"))
            {
                AnsiConsole.MarkupLine("[dim]Aborted.[/]");
                return 0;
            }

            Mission? restarted = await PostAsync<Mission>($"/api/v1/missions/{missionId}/restart", new
            {
                Title = settings.Title,
                Description = settings.Description
            }).ConfigureAwait(false);

            if (restarted != null)
            {
                AnsiConsole.MarkupLine($"[green]Mission restarted![/] Status: [bold]{restarted.Status}[/]");
                AnsiConsole.MarkupLine($"  Run [green]armada watch[/] to monitor progress.");
            }

            return 0;
        }

        private async Task<string> ResolveMissionIdAsync(string identifier)
        {
            if (identifier.StartsWith("msn_")) return identifier;

            EnumerationResult<Mission>? missionResult = await GetAsync<EnumerationResult<Mission>>("/api/v1/missions").ConfigureAwait(false);
            List<Mission>? missions = missionResult?.Objects;
            if (missions != null)
            {
                Mission? match = EntityResolver.ResolveMission(missions, identifier);
                if (match != null) return match.Id;
            }
            return identifier;
        }
    }
}
