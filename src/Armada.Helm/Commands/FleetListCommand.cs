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
    /// List all fleets.
    /// </summary>
    [Description("List all fleets")]
    public class FleetListCommand : BaseCommand<FleetListSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, FleetListSettings settings, CancellationToken cancellationToken)
        {
            string path = AppendPagination("/api/v1/fleets", settings);
            EnumerationResult<Fleet>? result = await GetAsync<EnumerationResult<Fleet>>(path).ConfigureAwait(false);

            if (result == null || result.Objects == null || result.Objects.Count == 0)
            {
                AnsiConsole.MarkupLine("[gold1]No fleets found.[/] Use [green]armada fleet add <name>[/] to create one.");
                return 0;
            }

            if (IsJsonMode(settings))
            {
                WriteJson(result);
                return 0;
            }

            TableRenderer.RenderPaginationHeader(result.PageNumber, result.TotalPages, result.TotalRecords, result.TotalMs);
            Table table = TableRenderer.CreateTable("Fleets", null);
            table.AddColumn("Id");
            table.AddColumn("Name");
            table.AddColumn("Active");
            table.AddColumn("Created");

            foreach (Fleet fleet in result.Objects)
            {
                string activeColor = fleet.Active ? "green" : "red";
                table.AddRow(
                    Markup.Escape(fleet.Id),
                    Markup.Escape(fleet.Name),
                    $"[{activeColor}]{fleet.Active}[/]",
                    fleet.CreatedUtc.ToString("yyyy-MM-dd HH:mm"));
            }

            AnsiConsole.Write(table);
            return 0;
        }
    }
}
