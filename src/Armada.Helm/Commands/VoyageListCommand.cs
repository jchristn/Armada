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

    /// <summary>
    /// List all voyages.
    /// </summary>
    [Description("List all voyages")]
    public class VoyageListCommand : BaseCommand<VoyageListSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, VoyageListSettings settings, CancellationToken cancellationToken)
        {
            string path = "/api/v1/voyages";
            if (!string.IsNullOrEmpty(settings.Status))
                path += "?status=" + settings.Status;
            path = AppendPagination(path, settings);

            EnumerationResult<Voyage>? result = await GetAsync<EnumerationResult<Voyage>>(path).ConfigureAwait(false);

            if (result == null || result.Objects == null || result.Objects.Count == 0)
            {
                AnsiConsole.MarkupLine("[gold1]No voyages found.[/] Start one with [green]armada go \"your task\"[/].");
                return 0;
            }

            if (IsJsonMode(settings))
            {
                WriteJson(result);
                return 0;
            }

            TableRenderer.RenderPaginationHeader(result.PageNumber, result.TotalPages, result.TotalRecords, result.TotalMs);
            Table table = TableRenderer.CreateTable("Voyages", null);
            table.AddColumn("Id");
            table.AddColumn("Title");
            table.AddColumn("Status");
            table.AddColumn("Created");

            foreach (Voyage voyage in result.Objects)
            {
                string statusColor = voyage.Status switch
                {
                    VoyageStatusEnum.Complete => "green",
                    VoyageStatusEnum.Failed => "red",
                    VoyageStatusEnum.InProgress => "gold1",
                    VoyageStatusEnum.Cancelled => "grey",
                    _ => "dodgerblue1"
                };

                table.AddRow(
                    $"[dim]{Markup.Escape(voyage.Id)}[/]",
                    $"[bold]{Markup.Escape(voyage.Title)}[/]",
                    $"[{statusColor}]{voyage.Status}[/]",
                    $"[dim]{voyage.CreatedUtc.ToString("yyyy-MM-dd HH:mm")}[/]");
            }

            AnsiConsole.Write(table);
            return 0;
        }
    }
}
