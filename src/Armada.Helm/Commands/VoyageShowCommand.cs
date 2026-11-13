namespace Armada.Helm.Commands
{
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
    /// Show voyage details. Accepts ID or title.
    /// </summary>
    [Description("Show voyage details")]
    public class VoyageShowCommand : BaseCommand<VoyageShowSettings>
    {
        /// <summary>
        /// Missions listed per voyage (the table title gives the total when there are more).
        /// </summary>
        public const int MissionPageSize = 100;

        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, VoyageShowSettings settings, CancellationToken cancellationToken)
        {
            Voyage? voyage = null;

            if (settings.Id.StartsWith("vyg_"))
            {
                // Server returns the voyage with mission counts (VoyageProgress); missions are paged separately.
                VoyageProgress? detail = await GetAsync<VoyageProgress>($"/api/v1/voyages/{settings.Id}").ConfigureAwait(false);
                if (detail?.Voyage != null)
                    voyage = detail.Voyage;
            }

            if (voyage == null)
            {
                EnumerationResult<Voyage>? allResult = await GetAsync<EnumerationResult<Voyage>>("/api/v1/voyages").ConfigureAwait(false);
                List<Voyage>? all = allResult?.Objects;
                if (all != null)
                    voyage = EntityResolver.ResolveVoyage(all, settings.Id);
            }

            if (voyage == null)
            {
                HelmConsole.Error.MarkupLine($"[red]Voyage not found:[/] {Markup.Escape(settings.Id)}");
                AnsiConsole.MarkupLine("List voyages with [green]armada voyage list[/].");
                return 1;
            }

            string statusColor = voyage.Status switch
            {
                VoyageStatusEnum.Complete => "green",
                VoyageStatusEnum.Failed => "red",
                VoyageStatusEnum.InProgress => "gold1",
                VoyageStatusEnum.Cancelled => "grey",
                _ => "dodgerblue1"
            };

            Panel panel = new Panel(
                new Rows(
                    new Markup($"[dodgerblue1]Id:[/]          [dim]{Markup.Escape(voyage.Id)}[/]"),
                    new Markup($"[dodgerblue1]Title:[/]       [bold]{Markup.Escape(voyage.Title)}[/]"),
                    new Markup($"[dodgerblue1]Status:[/]      [{statusColor}]{voyage.Status}[/]"),
                    new Markup($"[dodgerblue1]Created:[/]     {voyage.CreatedUtc:yyyy-MM-dd HH:mm}"),
                    new Markup($"[dodgerblue1]Description:[/] {Markup.Escape(voyage.Description ?? "-")}")));
            panel.Header = new PanelHeader("[bold dodgerblue1]Voyage Details[/]");
            panel.Border = BoxBorder.Rounded;
            panel.BorderStyle = new Style(Color.DodgerBlue1);

            AnsiConsole.Write(panel);

            // Fetch one page of this voyage's missions (oldest first); the title says when there are more.
            EnumerationResult<Mission>? missionResult = await GetAsync<EnumerationResult<Mission>>($"/api/v1/missions?voyageId={voyage.Id}&pageSize={MissionPageSize}&order=CreatedAscending").ConfigureAwait(false);
            List<Mission>? missions = missionResult?.Objects;

            if (missions != null && missions.Count > 0)
            {
                string missionTitle = missionResult!.TotalRecords > missions.Count ? $"Missions ({missions.Count} of {missionResult.TotalRecords})" : "Missions";
                Table table = TableRenderer.CreateTable(missionTitle, null);
                table.AddColumn("[bold]Id[/]");
                table.AddColumn("[bold]Title[/]");
                table.AddColumn("[bold]Status[/]");
                table.AddColumn("[bold]Captain[/]");

                foreach (Mission mission in missions)
                {
                    string missionColor = TableRenderer.MissionStatusColor(mission.Status.ToString());

                    table.AddRow(
                        $"[dim]{Markup.Escape(mission.Id)}[/]",
                        $"[bold]{Markup.Escape(mission.Title)}[/]",
                        $"[{missionColor}]{mission.Status}[/]",
                        Markup.Escape(mission.CaptainId ?? "-"));
                }

                AnsiConsole.Write(table);

                // Suggest retry if there are failed missions (counted over the whole voyage, not just this page)
                EnumerationResult<Mission>? failedResult = await GetAsync<EnumerationResult<Mission>>($"/api/v1/missions?voyageId={voyage.Id}&status=Failed&pageSize=1").ConfigureAwait(false);
                long failedCount = failedResult?.TotalRecords ?? missions.Count(m => m.Status == MissionStatusEnum.Failed);
                if (failedCount > 0)
                {
                    AnsiConsole.MarkupLine($"  [red]{failedCount} failed mission(s).[/] Retry with [green]armada voyage retry {Markup.Escape(voyage.Id)}[/].");
                }

                // Suggest viewing logs
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[dim]View mission details/logs with:[/]");
                foreach (Mission mission in missions)
                {
                    AnsiConsole.MarkupLine($"  [green]armada mission show {Markup.Escape(mission.Id)}[/]");
                }
            }

            return 0;
        }
    }
}
