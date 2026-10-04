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
    /// List all captains.
    /// </summary>
    [Description("List all captains")]
    public class CaptainListCommand : BaseCommand<CaptainListSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, CaptainListSettings settings, CancellationToken cancellationToken)
        {
            string path = AppendPagination("/api/v1/captains", settings);
            EnumerationResult<Captain>? result = await GetAsync<EnumerationResult<Captain>>(path).ConfigureAwait(false);

            if (result == null || result.Objects == null || result.Objects.Count == 0)
            {
                AnsiConsole.MarkupLine("[gold1]No captains found.[/]");
                AnsiConsole.MarkupLine("Captains are auto-created when you run [green]armada go \"your task\"[/], or add one manually with [green]armada captain add <name>[/].");
                return 0;
            }

            if (IsJsonMode(settings))
            {
                WriteJson(result);
                return 0;
            }

            // Fetch missions to look up titles for the Mission column
            Dictionary<string, Mission> missionLookup = new Dictionary<string, Mission>();
            EnumerationResult<Mission>? missionResult = await GetAsync<EnumerationResult<Mission>>("/api/v1/missions").ConfigureAwait(false);
            if (missionResult?.Objects != null)
            {
                foreach (Mission m in missionResult.Objects)
                {
                    missionLookup[m.Id] = m;
                }
            }

            TableRenderer.RenderPaginationHeader(result.PageNumber, result.TotalPages, result.TotalRecords, result.TotalMs);
            Table table = TableRenderer.CreateTable("Captains", null);
            table.AddColumn("[bold]Id[/]");
            table.AddColumn("[bold]Name[/]");
            table.AddColumn("[bold]Runtime[/]");
            table.AddColumn("[bold]State[/]");
            table.AddColumn("[bold]Mission[/]");
            table.AddColumn("[bold]Heartbeat[/]");

            foreach (Captain captain in result.Objects)
            {
                string stateColor = TableRenderer.CaptainStateColor(captain.State);

                string heartbeat = captain.LastHeartbeatUtc.HasValue
                    ? captain.LastHeartbeatUtc.Value.ToString("HH:mm:ss")
                    : "[dim]-[/]";

                string missionCell = "-";
                if (!string.IsNullOrEmpty(captain.CurrentMissionId))
                {
                    if (missionLookup.TryGetValue(captain.CurrentMissionId, out Mission? mission) && !string.IsNullOrEmpty(mission.Title))
                    {
                        missionCell = Markup.Escape(mission.Title) + "\n" + $"[dim]{Markup.Escape(captain.CurrentMissionId)}[/]";
                    }
                    else
                    {
                        missionCell = Markup.Escape(captain.CurrentMissionId);
                    }
                }

                table.AddRow(
                    $"[dim]{Markup.Escape(captain.Id)}[/]",
                    $"[bold]{Markup.Escape(captain.Name)}[/]",
                    $"{captain.Runtime}",
                    $"[{stateColor}]{captain.State}[/]",
                    missionCell,
                    heartbeat);
            }

            AnsiConsole.Write(table);
            return 0;
        }
    }
}
