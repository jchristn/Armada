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
    /// List missions with optional filters.
    /// </summary>
    [Description("List missions")]
    public class MissionListCommand : BaseCommand<MissionListSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, MissionListSettings settings, CancellationToken cancellationToken)
        {
            List<string> queryParams = new List<string>();
            if (!string.IsNullOrEmpty(settings.Status)) queryParams.Add("status=" + settings.Status);
            if (!string.IsNullOrEmpty(settings.Vessel)) queryParams.Add("vesselId=" + settings.Vessel);
            if (!string.IsNullOrEmpty(settings.Captain)) queryParams.Add("captainId=" + settings.Captain);
            if (!string.IsNullOrEmpty(settings.Voyage)) queryParams.Add("voyageId=" + settings.Voyage);

            string path = "/api/v1/missions";
            if (queryParams.Count > 0)
                path += "?" + string.Join("&", queryParams);
            path = AppendPagination(path, settings);

            EnumerationResult<Mission>? result = await GetAsync<EnumerationResult<Mission>>(path).ConfigureAwait(false);

            if (result == null || result.Objects == null || result.Objects.Count == 0)
            {
                AnsiConsole.MarkupLine("[gold1]No missions found.[/] Dispatch work with [green]armada go \"your task\"[/].");
                return 0;
            }

            if (IsJsonMode(settings))
            {
                WriteJson(result);
                return 0;
            }

            TableRenderer.RenderPaginationHeader(result.PageNumber, result.TotalPages, result.TotalRecords, result.TotalMs);
            Table table = TableRenderer.CreateTable("Missions", null);
            table.AddColumn("Id");
            table.AddColumn("Title");
            table.AddColumn("Vessel");
            table.AddColumn("Captain");
            table.AddColumn("Status");
            table.AddColumn("Created");

            foreach (Mission mission in result.Objects)
            {
                string statusColor = TableRenderer.MissionStatusColor(mission.Status.ToString());

                table.AddRow(
                    $"[dim]{Markup.Escape(mission.Id)}[/]",
                    $"[bold]{Markup.Escape(mission.Title)}[/]",
                    Markup.Escape(mission.VesselId ?? "-"),
                    Markup.Escape(mission.CaptainId ?? "-"),
                    $"[{statusColor}]{mission.Status}[/]",
                    $"[dim]{mission.CreatedUtc.ToString("yyyy-MM-dd HH:mm")}[/]");
            }

            AnsiConsole.Write(table);
            return 0;
        }
    }
}
