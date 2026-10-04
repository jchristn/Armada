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
    /// Cancel a mission. Accepts ID or title.
    /// </summary>
    [Description("Cancel a mission")]
    public class MissionCancelCommand : BaseCommand<MissionCancelSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, MissionCancelSettings settings, CancellationToken cancellationToken)
        {
            string missionId = await ResolveMissionIdAsync(settings.Id).ConfigureAwait(false);

            if (!AnsiConsole.Confirm($"Cancel mission [bold]{Markup.Escape(missionId)}[/]?"))
            {
                AnsiConsole.MarkupLine("[dim]Aborted.[/]");
                return 0;
            }

            try
            {
                await DeleteAsync($"/api/v1/missions/{missionId}").ConfigureAwait(false);
                AnsiConsole.MarkupLine($"[gold1]Mission {Markup.Escape(missionId)} cancelled.[/]");
            }
            catch (HttpRequestException)
            {
                AnsiConsole.MarkupLine("[red]Mission not found or could not be cancelled.[/]");
                AnsiConsole.MarkupLine("List missions with [green]armada mission list[/].");
                return 1;
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
